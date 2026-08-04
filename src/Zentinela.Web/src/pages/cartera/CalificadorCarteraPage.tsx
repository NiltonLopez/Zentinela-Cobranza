import { useEffect, useState } from 'react';
import { useQuery } from '@tanstack/react-query';
import {
  calificarCartera,
  obtenerOpcionesCalificador,
  descargarLocalizacionCsv,
  type CategoriaLocalizacion,
} from '../../api/calificadorApi';
import type { CalificadorCarteraDto, DesglosePar, LocalizacionFuente } from '../../types/calificador';
import { CargandoSpiral } from '../../components/CargandoSpiral';
import { ZDropdown } from '../../components/ZDropdown';

// Mapa etiqueta de fuente (UI) → slug que entiende el backend para descargar
// (Cédula, Nombre, Última Consulta) de la celda correspondiente.
const FUENTE_SLUG: Record<string, string> = {
  'Cajacopi':       'cajacopi',
  'Cámara':         'camara',
  'Salud Total':    'salud-total',
  'Fosyga':         'fosyga',
  'Compensados':    'compensados',
  'Positiva':       'positiva',
  'Tigo':           'tigo',
  'TigoUne':        'tigoune',
  'Coosalud':       'coosalud',
  'SOS':            'sos',
  'Claro':          'claro',
  'MiVacuna':       'mivacuna',
  'MutualSer':      'mutualser',
  'NuevaEps':       'nuevaeps',
  'Sura (ARL)':     'sura',
  'Positiva Emp.':  'positiva-empresa',
  'RUES':           'rues',
  'SIMIT':          'simit',
  'RGM':            'rgm',
};

// Ícono de descarga minimalista (16×16, currentColor). Mismo enfoque que IconoLapiz
// en GestionPersonalPage: solo se ve en hover y hereda color del padre.
function IconoDescarga({ size = 12 }: { size?: number }) {
  return (
    <svg
      width={size} height={size} viewBox="0 0 16 16" fill="none"
      stroke="currentColor" strokeWidth={1.5} strokeLinecap="round" strokeLinejoin="round"
      aria-hidden="true"
    >
      <path d="M8 2v8" />
      <path d="M4 7l4 4 4-4" />
      <path d="M2.5 13.5h11" />
    </svg>
  );
}

// ── Helpers de formato ────────────────────────────────────────────────────────

function fmt(n: number): string {
  return n.toLocaleString('es-CO');
}

function fmtCOP(n: number): string {
  return new Intl.NumberFormat('es-CO', {
    style: 'currency',
    currency: 'COP',
    maximumFractionDigits: 0,
  }).format(n);
}

function pct(parte: number, total: number): string {
  if (total === 0) return '0%';
  return (parte / total * 100).toFixed(1) + '%';
}

// ── Componentes internos ──────────────────────────────────────────────────────

function Tarjeta({
  titulo,
  children,
  style,
}: {
  titulo: string;
  children: React.ReactNode;
  style?: React.CSSProperties;
}) {
  return (
    <div style={{
      background: 'var(--z-bg-card)',
      border: '0.5px solid var(--z-border)',
      borderRadius: 8,
      padding: '20px 24px',
      ...style,
    }}>
      <p style={{
        fontSize: 11,
        fontWeight: 700,
        textTransform: 'uppercase',
        letterSpacing: '0.08em',
        color: 'var(--z-text-muted)',
        marginBottom: 16,
      }}>
        {titulo}
      </p>
      {children}
    </div>
  );
}

function KPI({ label, value }: { label: string; value: string | number }) {
  return (
    <div style={{ marginBottom: 8 }}>
      <span style={{ color: 'var(--z-text-muted)', fontSize: 13 }}>{label}</span>
      <span style={{
        display: 'block',
        fontSize: 22,
        fontWeight: 700,
        color: 'var(--z-text-primary)',
      }}>
        {typeof value === 'number' ? fmt(value) : value}
      </span>
    </div>
  );
}

function BarraDesglose({ filas, total }: { filas: DesglosePar[]; total: number }) {
  if (filas.length === 0)
    return <p style={{ color: 'var(--z-text-muted)', fontSize: 13 }}>Sin datos</p>;

  return (
    <div style={{ display: 'flex', flexDirection: 'column', gap: 8 }}>
      {filas.map((f) => (
        <div key={f.etiqueta}>
          <div style={{
            display: 'flex',
            justifyContent: 'space-between',
            fontSize: 13,
            marginBottom: 3,
          }}>
            <span style={{ color: 'var(--z-text-primary)' }}>{f.etiqueta}</span>
            <span style={{ color: 'var(--z-text-muted)' }}>
              {fmt(f.cantidad)} ({pct(f.cantidad, total)})
            </span>
          </div>
          <div style={{
            height: 6,
            background: 'var(--z-bg-page)',
            borderRadius: 3,
            overflow: 'hidden',
          }}>
            <div style={{
              height: '100%',
              width: `${Math.min(f.porcentaje, 100)}%`,
              background: 'var(--z-brand)',
              borderRadius: 3,
            }} />
          </div>
        </div>
      ))}
    </div>
  );
}

// Persistencia en sessionStorage de la cartera seleccionada: como el fetch de
// calificarCartera no se cancela al desmontar (no usa AbortSignal), sigue
// corriendo en segundo plano si el usuario cambia de submódulo. Sin esto, al
// volver el componente remonta con la selección en blanco y "se corta" el
// resultado — con esto reconecta al mismo queryKey y React Query muestra lo
// que ya haya en caché (o el spinner, si la petición seguía en curso).
// Exportada para que AuthContext.cerrarSesion() la borre al cerrar sesión —
// la persistencia es para sobrevivir a la navegación entre submódulos dentro
// de la misma sesión, no para que la siguiente sesión en el mismo navegador
// vea la cartera de otro usuario.
export const CLAVE_ACTIVA = 'calificador-cartera-activa';

function leerSeleccionGuardada(): string {
  try {
    return sessionStorage.getItem(CLAVE_ACTIVA) ?? '';
  } catch {
    return ''; // sessionStorage no disponible (ej. modo privado) — no bloquea la página
  }
}

// ── Página principal ──────────────────────────────────────────────────────────

export function CalificadorCarteraPage() {
  const [seleccion, setSeleccion] = useState(leerSeleccionGuardada);

  useEffect(() => {
    try {
      if (seleccion) sessionStorage.setItem(CLAVE_ACTIVA, seleccion);
      else sessionStorage.removeItem(CLAVE_ACTIVA);
    } catch {
      // sessionStorage no disponible — la selección simplemente no sobrevive a la navegación
    }
  }, [seleccion]);

  const { data: opciones, isLoading: cargandoOpciones } = useQuery({
    queryKey: ['calificador-opciones'],
    queryFn: obtenerOpcionesCalificador,
  });

  // El value del select es "carteraID_liderID" o "carteraID_" cuando es Cartera Completo
  // (liderID null en el backend). El segundo segmento vacío se convierte a NaN → null.
  const [carteraIDRaw, liderIDRaw] = seleccion ? seleccion.split('_') : ['', ''];
  const carteraID: number | null = carteraIDRaw ? Number(carteraIDRaw) : null;
  const liderID:   number | null = liderIDRaw   ? Number(liderIDRaw)   : null;

  const { data, isLoading, isError } = useQuery({
    queryKey: ['calificador', carteraID, liderID],
    queryFn: ({ signal }) => calificarCartera(carteraID!, liderID, signal),
    enabled: carteraID != null,
    retry: false,
    // Esta consulta puede tardar varios minutos en carteras grandes. Con el
    // AbortSignal ya conectado de verdad (antes era un no-op), el refetch
    // automático al recuperar foco cancelaría el intento en curso y lo
    // reiniciaría desde cero cada vez que el usuario vuelve a la pestaña —
    // convirtiendo una espera larga en una espera infinita.
    refetchOnWindowFocus: false,
    refetchOnReconnect: false,
  });

  return (
    <div style={{ maxWidth: 1300 }}>
      {/* Encabezado */}
      <div style={{ display: 'flex', justifyContent: 'space-between', alignItems: 'flex-start', flexWrap: 'wrap', gap: 12, marginBottom: 24 }}>
        <div>
          <h1 style={{ fontSize: 20, fontWeight: 600, color: 'var(--z-text-primary)', margin: 0 }}>
            Calificador de Cartera
          </h1>
          <p style={{ fontSize: 13, color: 'var(--z-text-muted)', margin: '4px 0 0' }}>
            Analiza el perfil de una cartera: volumen, saldos, contactabilidad, tipificación y localización.
          </p>
        </div>
      </div>

      {/* Selector */}
      <div style={{ marginBottom: 28 }}>
        <label style={{ fontSize: '13px', fontWeight: 600, display: 'block', marginBottom: 6, color: 'var(--z-text-primary)' }}>
          Cartera a analizar
        </label>
        {cargandoOpciones ? (
          <CargandoSpiral />
        ) : (
          <ZDropdown
            options={[
              { value: '', label: '— Selecciona una cartera —' },
              ...(opciones ?? []).map((o) => ({
                value: `${o.carteraID}_${o.liderID ?? ''}`,
                label: o.etiqueta,
              })),
            ]}
            value={seleccion}
            onChange={setSeleccion}
            style={{ minWidth: 320 }}
          />
        )}
      </div>

      {/* Estado vacío */}
      {!seleccion && (
        <div style={{
          textAlign: 'center',
          padding: '64px 0',
          color: 'var(--z-text-muted)',
          fontSize: '13px',
        }}>
          Selecciona una cartera para ver su calificación.
        </div>
      )}

      {/* Cargando métricas */}
      {seleccion && isLoading && (
        <div style={{ display: 'flex', justifyContent: 'center', padding: 48 }}>
          <CargandoSpiral />
        </div>
      )}

      {/* Error */}
      {isError && (
        <div style={{
          background: '#fff5f5',
          border: '0.5px solid #fecaca',
          borderRadius: 8,
          padding: '14px 16px',
          fontSize: '13px',
          color: '#dc2626',
        }}>
          No se pudo cargar la información. Intenta de nuevo.
        </div>
      )}

      {/* Métricas */}
      {data && carteraID != null && (
        <Metricas
          d={data}
          carteraID={carteraID}
          liderID={liderID}
          etiquetaCartera={
            opciones?.find(o => o.carteraID === carteraID && (o.liderID ?? null) === liderID)?.etiqueta
              ?? `Cartera_${carteraID}`
          }
        />
      )}
    </div>
  );
}

// Estado operativo de cada fuente de localización (visual, no afecta lógica).
// 'Funcional' = se actualiza con regularidad; 'Intermitente' = irregular;
// 'Inactivo' = sin actualizaciones recientes.
const ESTADO_FUENTE: Record<string, { etiqueta: string; color: string; nota?: string }> = {
  'Cajacopi':         { etiqueta: 'Funcional',    color: '#16A34A' },
  'Cámara':           { etiqueta: 'Intermitente', color: '#CA8A04' },
  'Salud Total':      { etiqueta: 'Funcional',    color: '#16A34A' },
  'Fosyga':           { etiqueta: 'Intermitente', color: '#CA8A04' },
  'Compensados':      { etiqueta: 'Intermitente', color: '#CA8A04' },
  'Positiva':         { etiqueta: 'Funcional',    color: '#16A34A' },
  'Tigo':             { etiqueta: 'Inactivo',     color: '#DC2626' },
  'TigoUne':          { etiqueta: 'Inactivo',     color: '#DC2626', nota: 'sin datos cargados aún' },
  'Coosalud':         { etiqueta: 'Inactivo',     color: '#DC2626', nota: 'sin datos cargados aún' },
  'SOS':              { etiqueta: 'Funcional',    color: '#16A34A' },
  'Claro':            { etiqueta: 'Inactivo',     color: '#DC2626' },
  'MiVacuna':         { etiqueta: 'Funcional',    color: '#16A34A' },
  'MutualSer':        { etiqueta: 'Funcional',    color: '#16A34A' },
  'NuevaEps':         { etiqueta: 'Funcional',    color: '#16A34A' },
  'Sura (ARL)':       { etiqueta: 'Funcional',    color: '#16A34A', nota: 'solo nombre de empresa' },
  'Positiva Emp.':    { etiqueta: 'Funcional',    color: '#16A34A' },
  'RUES':             { etiqueta: 'Funcional',    color: '#16A34A' },
  'SIMIT':            { etiqueta: 'Funcional',    color: '#16A34A' },
  'RGM':              { etiqueta: 'Funcional',    color: '#16A34A' },
};

function TablaLocalizacion({
  filas,
  total,
  carteraID,
  liderID,
  etiquetaCartera,
}: {
  filas: LocalizacionFuente[];
  total: number;
  carteraID: number;
  liderID: number | null;
  etiquetaCartera: string;
}) {
  // Identificador único de la celda en hover: "{fuente}|{categoria}".
  // null = ninguna celda con hover. Cambia el cursor + revela el ícono.
  const [celdaHover, setCeldaHover] = useState<string | null>(null);

  // Slug de la celda en descarga (mismo formato). Se usa para mostrar
  // un spinner y desactivar el click mientras corre la consulta.
  const [celdaDescargando, setCeldaDescargando] = useState<string | null>(null);

  if (filas.length === 0)
    return <p style={{ color: 'var(--z-text-muted)', fontSize: 13 }}>Sin datos</p>;

  // Lanza la descarga del CSV de una celda. Usa el nombre que envía el
  // backend en Content-Disposition; como fallback construye uno con la
  // etiqueta de la cartera. Mismo patrón que Creador → dispararDescarga.
  const descargar = async (fuente: string, categoria: CategoriaLocalizacion) => {
    const slug = FUENTE_SLUG[fuente];
    if (!slug) return;

    const cellKey = `${fuente}|${categoria}`;
    if (celdaDescargando) return; // no permitir disparos en paralelo
    setCeldaDescargando(cellKey);

    try {
      const blob = await descargarLocalizacionCsv(carteraID, liderID, slug, categoria);
      const url = URL.createObjectURL(blob);
      const a = document.createElement('a');
      a.href = url;
      a.download =
        `Localizacion_${etiquetaCartera.replace(/\s+/g, '_')}_${slug}_${categoria}.csv`;
      document.body.appendChild(a);
      a.click();
      document.body.removeChild(a);
      URL.revokeObjectURL(url);
    } finally {
      setCeldaDescargando(null);
    }
  };

  // Celda numérica reutilizable: muestra la cifra + porcentaje y revela el
  // ícono de descarga al hacer hover. Click sobre el ícono (o sobre toda
  // la celda) dispara el CSV de esa intersección (fuente, categoría).
  const CeldaCifra = ({
    fuente,
    categoria,
    valor,
    porcentaje,
    colorTexto,
    porcentajeMuted = true,
  }: {
    fuente: string;
    categoria: CategoriaLocalizacion;
    valor: number;
    porcentaje: number;
    colorTexto: string;
    porcentajeMuted?: boolean;
  }) => {
    const cellKey = `${fuente}|${categoria}`;
    const hover = celdaHover === cellKey;
    const descargando = celdaDescargando === cellKey;
    const habilitado = FUENTE_SLUG[fuente] !== undefined && valor > 0;

    return (
      <td
        style={{
          padding: '8px 10px',
          textAlign: 'center',
          color: colorTexto,
          cursor: habilitado ? 'pointer' : 'default',
          position: 'relative',
        }}
        onMouseEnter={() => habilitado && setCeldaHover(cellKey)}
        onMouseLeave={() => setCeldaHover(prev => (prev === cellKey ? null : prev))}
        onClick={() => habilitado && !descargando && descargar(fuente, categoria)}
      >
        <span style={{ display: 'inline-flex', alignItems: 'center', gap: 5 }}>
          {fmt(valor)}
          <span style={{ color: porcentajeMuted ? 'var(--z-text-muted)' : 'inherit' }}>
            ({porcentaje.toFixed(1)}%)
          </span>
          {habilitado && (
            <span
              aria-label={`Descargar ${categoria} de ${fuente}`}
              style={{
                display: 'inline-flex',
                alignItems: 'center',
                color: colorTexto,
                opacity: hover || descargando ? 1 : 0,
                transition: 'opacity 0.15s',
                marginLeft: 2,
              }}
            >
              {descargando ? '…' : <IconoDescarga />}
            </span>
          )}
        </span>
      </td>
    );
  };

  return (
    <div style={{ overflowX: 'auto' }}>
      <table style={{ width: '100%', borderCollapse: 'collapse', fontSize: 13 }}>
        <thead>
          <tr style={{ borderBottom: '1px solid var(--z-border)' }}>
            {['Fuente', 'Encontrado', 'No encontró', 'Falta por buscar', 'Cobertura'].map((h) => (
              <th
                key={h}
                style={{
                  padding: '6px 10px',
                  textAlign: h === 'Fuente' ? 'left' : 'center',
                  color: 'var(--z-text-muted)',
                  fontWeight: 600,
                  whiteSpace: 'nowrap',
                }}
              >
                {h}
              </th>
            ))}
          </tr>
        </thead>
        <tbody>
          {filas.map((f) => {
            const pctEnc  = total > 0 ? (f.encontrados / total) * 100 : 0;
            const pctNoEnc = total > 0 ? (f.noEncontrados / total) * 100 : 0;
            const pctFalta = total > 0 ? (f.faltanPorBuscar / total) * 100 : 0;

            return (
              <tr
                key={f.fuente}
                style={{ borderBottom: '1px solid var(--z-border)' }}
              >
                <td style={{ padding: '8px 10px', fontWeight: 600 }}>
                  {f.fuente}
                  {ESTADO_FUENTE[f.fuente] && (
                    <span
                      style={{
                        marginLeft: 6,
                        fontSize: 10,
                        fontWeight: 500,
                        color: ESTADO_FUENTE[f.fuente].color,
                      }}
                    >
                      ({ESTADO_FUENTE[f.fuente].etiqueta}
                      {ESTADO_FUENTE[f.fuente].nota ? `, ${ESTADO_FUENTE[f.fuente].nota}` : ''})
                    </span>
                  )}
                </td>

                <CeldaCifra
                  fuente={f.fuente} categoria="encontrado"
                  valor={f.encontrados} porcentaje={pctEnc}
                  colorTexto="#15803D"
                />

                <CeldaCifra
                  fuente={f.fuente} categoria="no-encontrado"
                  valor={f.noEncontrados} porcentaje={pctNoEnc}
                  colorTexto="#B45309"
                />

                <CeldaCifra
                  fuente={f.fuente} categoria="falta"
                  valor={f.faltanPorBuscar} porcentaje={pctFalta}
                  colorTexto="var(--z-text-muted)"
                  porcentajeMuted={false}
                />

                {/* Barra apilada */}
                <td style={{ padding: '8px 10px', minWidth: 120 }}>
                  <div style={{
                    height: 8,
                    borderRadius: 4,
                    overflow: 'hidden',
                    display: 'flex',
                    background: 'var(--z-bg-page)',
                  }}>
                    <div style={{ width: `${pctEnc}%`, background: '#16A34A' }} />
                    <div style={{ width: `${pctNoEnc}%`, background: '#F59E0B' }} />
                    <div style={{ width: `${pctFalta}%`, background: '#D1D5DB' }} />
                  </div>
                </td>
              </tr>
            );
          })}
        </tbody>
      </table>

      {/* Leyenda */}
      <div style={{ display: 'flex', gap: 20, marginTop: 12, fontSize: 12, color: 'var(--z-text-muted)' }}>
        {[
          { color: '#16A34A', label: 'Encontrado' },
          { color: '#F59E0B', label: 'No encontró' },
          { color: '#D1D5DB', label: 'Falta por buscar' },
        ].map(({ color, label }) => (
          <span key={label} style={{ display: 'flex', alignItems: 'center', gap: 5 }}>
            <span style={{ width: 10, height: 10, borderRadius: 2, background: color, display: 'inline-block' }} />
            {label}
          </span>
        ))}
      </div>

      <p style={{ marginTop: 10, fontSize: 11, color: 'var(--z-text-muted)' }}>
        Pasa el cursor sobre cualquier cifra y haz click para descargar la lista (Cédula, Nombre, Última Consulta).
      </p>
    </div>
  );
}

function Metricas({
  d,
  carteraID,
  liderID,
  etiquetaCartera,
}: {
  d: CalificadorCarteraDto;
  carteraID: number;
  liderID: number | null;
  etiquetaCartera: string;
}) {
  return (
    <div style={{
      display: 'grid',
      gridTemplateColumns: '1fr 1fr',
      gap: 16,
    }}>

      {/* Banner gestión no disponible — ancho completo, lo primero */}
      {!d.gestionDisponible && (
        <div style={{
          gridColumn: '1 / -1',
          padding: '12px 18px',
          background: '#FEF9C3',
          border: '1px solid #FDE047',
          borderRadius: 8,
          fontSize: 13,
          color: '#713F12',
        }}>
          Sin datos de gestión aún — los KPIs de cobro se mostrarán cuando haya actividad registrada.
        </div>
      )}

      {/* Volumen */}
      <Tarjeta titulo="Volumen">
        <KPI label="Titulares en la cartera" value={d.totalTitulares} />
      </Tarjeta>

      {/* Saldo promedio */}
      <Tarjeta titulo="Saldo Promedio">
        <KPI label="Promedio por titular" value={fmtCOP(d.saldoPromedio)} />
      </Tarjeta>

      {/* Gestión */}
      {d.gestionDisponible && (
        <Tarjeta titulo="Gestión">
          <div style={{ display: 'grid', gridTemplateColumns: '1fr 1fr', gap: 12 }}>
            <KPI label="Gestionados" value={d.titularesGestionados} />
            <KPI label="Con acuerdo vigente" value={d.titularesConAcuerdoVigente} />
            <KPI label="Saldo total" value={fmtCOP(d.saldoTotal)} />
            <KPI label="Saldo promedio" value={fmtCOP(d.saldoPromedio)} />
            <KPI label="Valor promesas" value={fmtCOP(d.valorTotalPromesas)} />
          </div>
        </Tarjeta>
      )}

      {/* Rotación */}
      <Tarjeta titulo="Rotación de la cartera" style={{ gridColumn: '1 / -1' }}>
        <div style={{ display: 'flex', alignItems: 'baseline', gap: 8, marginBottom: 16 }}>
          <span style={{ fontSize: 13, color: 'var(--z-text-muted)' }}>Promedio de rotación</span>
          <span style={{ fontSize: 28, fontWeight: 700 }}>
            {d.rotacionPromedio != null ? d.rotacionPromedio : '—'}
          </span>
        </div>
        <BarraDesglose filas={d.desgloseRotacion} total={d.totalTitulares} />
      </Tarjeta>

      {/* Tipificación */}
      <Tarjeta titulo="Último perfil (tipificación)">
        <BarraDesglose filas={d.desglosePerfil} total={d.totalTitulares} />
      </Tarjeta>

      {/* Fosyga */}
      <Tarjeta titulo={`Fosyga — ${fmt(d.titularesConFosyga)} con estado (${pct(d.titularesConFosyga, d.totalTitulares)})`}>
        <BarraDesglose filas={d.desgloseTipoAfiliado} total={d.totalTitulares} />
      </Tarjeta>

      {/* Régimen */}
      <Tarjeta titulo="Régimen">
        <BarraDesglose filas={d.desgloseRegimen} total={d.totalTitulares} />
      </Tarjeta>

      {/* EPS */}
      <Tarjeta titulo="EPS (top 10)">
        <BarraDesglose filas={d.desgloseEPS} total={d.totalTitulares} />
      </Tarjeta>

      {/* Departamento */}
      <Tarjeta titulo="Departamento">
        <BarraDesglose filas={d.desgloseDepartamento} total={d.totalTitulares} />
      </Tarjeta>

      {/* Contactabilidad */}
      <Tarjeta titulo="Contactabilidad">
        <div style={{ display: 'grid', gridTemplateColumns: '1fr 1fr', gap: 12, marginBottom: 16 }}>
          <KPI label="Con celular" value={`${fmt(d.conCelular)} (${pct(d.conCelular, d.totalTitulares)})`} />
          <KPI label="Con email" value={`${fmt(d.conEmail)} (${pct(d.conEmail, d.totalTitulares)})`} />
          <KPI label="Con ambos" value={`${fmt(d.conCelularYEmail)} (${pct(d.conCelularYEmail, d.totalTitulares)})`} />
          <KPI label="Sin contacto" value={`${fmt(d.sinContacto)} (${pct(d.sinContacto, d.totalTitulares)})`} />
        </div>
        <p style={{
          fontSize: 11,
          fontWeight: 700,
          textTransform: 'uppercase',
          letterSpacing: '0.08em',
          color: 'var(--z-text-muted)',
          marginBottom: 10,
        }}>
          Celular por fuente
        </p>
        <BarraDesglose filas={d.desgloseFuentesCelular} total={d.totalTitulares} />
      </Tarjeta>

      {/* Edad — ancho completo */}
      <Tarjeta titulo="Edad — Registraduría" style={{ gridColumn: '1 / -1' }}>
        <p style={{ fontSize: 12, color: 'var(--z-text-muted)', marginBottom: 14 }}>
          Solo aparecen personas mayores de 33 años.
        </p>

        <div style={{ marginBottom: 14 }}>
          <div style={{ display: 'flex', justifyContent: 'space-between', fontSize: 13, marginBottom: 4 }}>
            <span style={{ color: '#15803D' }}>
              Con edad &nbsp;
              <strong>{fmt(d.conEdadRegistraduria)}</strong>
              &nbsp;({pct(d.conEdadRegistraduria, d.totalTitulares)})
            </span>
            <span style={{ color: 'var(--z-text-muted)' }}>
              Sin aparición &nbsp;
              <strong>{fmt(d.sinEdadRegistraduria)}</strong>
              &nbsp;({pct(d.sinEdadRegistraduria, d.totalTitulares)})
            </span>
          </div>
          <div style={{ height: 10, borderRadius: 5, overflow: 'hidden', display: 'flex', background: '#E5E7EB' }}>
            <div style={{
              width: pct(d.conEdadRegistraduria, d.totalTitulares),
              background: '#16A34A',
              borderRadius: '5px 0 0 5px',
            }} />
          </div>
        </div>

        <div style={{ display: 'flex', justifyContent: 'space-between', alignItems: 'baseline' }}>
          <span style={{ fontSize: 13, color: 'var(--z-text-muted)' }}>Promedio (entre quienes aparecen)</span>
          <span style={{ fontSize: 22, fontWeight: 700 }}>
            {d.edadPromedioRegistraduria != null ? `${d.edadPromedioRegistraduria} años` : '—'}
          </span>
        </div>
      </Tarjeta>

      {/* Localización — ancho completo */}
      <Tarjeta titulo="Porcentajes de Localización" style={{ gridColumn: '1 / -1' }}>
        <TablaLocalizacion
          filas={d.localizacion}
          total={d.totalTitulares}
          carteraID={carteraID}
          liderID={liderID}
          etiquetaCartera={etiquetaCartera}
        />
      </Tarjeta>

    </div>
  );
}
