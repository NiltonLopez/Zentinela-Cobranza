import { useState } from 'react';
import { useQuery } from '@tanstack/react-query';
import { calificarCartera, obtenerOpcionesCalificador } from '../../api/calificadorApi';
import type { CalificadorCarteraDto, DesglosePar, LocalizacionFuente } from '../../types/calificador';
import { CargandoSpiral } from '../../components/CargandoSpiral';

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

// ── Página principal ──────────────────────────────────────────────────────────

export function CalificadorCarteraPage() {
  const [seleccion, setSeleccion] = useState('');

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
    queryFn: () => calificarCartera(carteraID!, liderID),
    enabled: carteraID != null,
  });

  return (
    <div style={{ maxWidth: 1200 }}>
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
          <select
            value={seleccion}
            onChange={(e) => setSeleccion(e.target.value)}
            style={{
              padding: '8px 12px',
              borderRadius: 6,
              border: '0.5px solid var(--z-border)',
              background: 'var(--z-bg-card)',
              color: 'var(--z-text-primary)',
              fontSize: '13px',
              minWidth: 320,
              cursor: 'pointer',
            }}
          >
            <option value="">— Selecciona una cartera —</option>
            {opciones?.map((o) => (
              <option key={`${o.carteraID}_${o.liderID ?? ''}`} value={`${o.carteraID}_${o.liderID ?? ''}`}>
                {o.etiqueta}
              </option>
            ))}
          </select>
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
      {data && <Metricas d={data} />}
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
  'SOS':              { etiqueta: 'Funcional',    color: '#16A34A' },
  'Claro':            { etiqueta: 'Inactivo',     color: '#DC2626' },
  'MutualSer':        { etiqueta: 'Funcional',    color: '#16A34A' },
  'Sura (ARL)':       { etiqueta: 'Funcional',    color: '#16A34A', nota: 'solo nombre de empresa' },
  'Positiva Emp.':    { etiqueta: 'Funcional',    color: '#16A34A' },
  'RUES':             { etiqueta: 'Funcional',    color: '#16A34A' },
  'SIMIT':            { etiqueta: 'Funcional',    color: '#16A34A' },
  'RGM':              { etiqueta: 'Funcional',    color: '#16A34A' },
};

function TablaLocalizacion({
  filas,
  total,
}: {
  filas: LocalizacionFuente[];
  total: number;
}) {
  if (filas.length === 0)
    return <p style={{ color: 'var(--z-text-muted)', fontSize: 13 }}>Sin datos</p>;

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

                <td style={{ padding: '8px 10px', textAlign: 'center', color: '#15803D' }}>
                  {fmt(f.encontrados)}
                  <span style={{ color: 'var(--z-text-muted)', marginLeft: 4 }}>
                    ({pctEnc.toFixed(1)}%)
                  </span>
                </td>

                <td style={{ padding: '8px 10px', textAlign: 'center', color: '#B45309' }}>
                  {fmt(f.noEncontrados)}
                  <span style={{ color: 'var(--z-text-muted)', marginLeft: 4 }}>
                    ({pctNoEnc.toFixed(1)}%)
                  </span>
                </td>

                <td style={{ padding: '8px 10px', textAlign: 'center', color: 'var(--z-text-muted)' }}>
                  {fmt(f.faltanPorBuscar)}
                  <span style={{ marginLeft: 4 }}>
                    ({pctFalta.toFixed(1)}%)
                  </span>
                </td>

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
    </div>
  );
}

function Metricas({ d }: { d: CalificadorCarteraDto }) {
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
        <TablaLocalizacion filas={d.localizacion} total={d.totalTitulares} />
      </Tarjeta>

    </div>
  );
}
