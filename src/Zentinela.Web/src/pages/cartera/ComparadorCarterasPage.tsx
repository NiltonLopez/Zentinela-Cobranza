import { useState } from 'react';
import { useQuery } from '@tanstack/react-query';
import {
  compararCartera,
  descargarExcelComparativo,
  descargarExcelCompleto,
  obtenerOpcionesComparador,
} from '../../api/comparadorApi';
import type { ComparadorVsCarteraDto, TitularComparativoDto } from '../../types/comparador';
import { CargandoSpiral } from '../../components/CargandoSpiral';

// ── Helpers ───────────────────────────────────────────────────────────────────

function fmt(n: number): string {
  return n.toLocaleString('es-CO');
}

function fmtCOP(n: number): string {
  return new Intl.NumberFormat('es-CO', {
    style: 'currency', currency: 'COP', maximumFractionDigits: 0,
  }).format(n);
}

function fmtFecha(s: string | null): string {
  if (!s) return '—';
  const d = new Date(s);
  return isNaN(d.getTime()) ? s : d.toLocaleDateString('es-CO');
}

// ── Componentes ───────────────────────────────────────────────────────────────

// Espejo de PerfilClasificacion.cs (backend). Si se agregan códigos allá,
// agregarlos también acá para que la chip salga del color correcto.
const PERFILES_PAZ_Y_SALVO = new Set([
  'paz y salvo', 'consignación por aplicar', 'pago total', 'normalizado',
]);
const PERFILES_CONTACTO_POSITIVO = new Set([
  'acuerdo de pago abono', 'acuerdo de pago total', 'realizo abono', 'recordar',
  'incumplido', 'no hubo acuerdo', 'potencial acuerdo', 'renuente', 'no gestionar',
  'contacto positivo', 'promesa de pago', 'acuerdo',
]);

function ChipPerfil({ perfil }: { perfil: string | null }) {
  if (!perfil) return <span style={{ color: 'var(--z-text-muted)', fontSize: 12 }}>Sin dato</span>;

  const key = perfil.toLowerCase();
  const color =
    PERFILES_PAZ_Y_SALVO.has(key)        ? '#22C55E' :
    PERFILES_CONTACTO_POSITIVO.has(key)  ? '#3B82F6' :
    '#EF4444';

  return (
    <span style={{
      fontSize: 11, fontWeight: 600, padding: '2px 8px',
      borderRadius: 12, background: `${color}22`, color, whiteSpace: 'nowrap',
    }}>
      {perfil}
    </span>
  );
}

function TablaTitulares({
  titulares,
  nombreSeleccionada,
  nombreOtra,
}: {
  titulares: TitularComparativoDto[];
  nombreSeleccionada: string;
  nombreOtra: string;
}) {
  const cols = [
    'Número Doc.', 'Nombre',
    `Perfil en ${nombreSeleccionada}`,
    `Perfil en ${nombreOtra}`,
    `Tel. ${nombreSeleccionada}`,
    `Tel. ${nombreOtra}`,
    'Saldo', 'Última gestión', 'Valor promesa',
  ];

  return (
    <div style={{ overflowX: 'auto', marginTop: 12 }}>
      <table style={{ width: '100%', borderCollapse: 'collapse', fontSize: 12 }}>
        <thead>
          <tr style={{ borderBottom: '2px solid var(--z-border)' }}>
            {cols.map(c => (
              <th key={c} style={{
                padding: '6px 10px', textAlign: 'left', fontWeight: 700,
                fontSize: 11, textTransform: 'uppercase', letterSpacing: '0.05em',
                color: 'var(--z-text-muted)', whiteSpace: 'nowrap',
              }}>
                {c}
              </th>
            ))}
          </tr>
        </thead>
        <tbody>
          {titulares.map(t => (
            <tr key={t.numeroDocumento} style={{ borderBottom: '1px solid var(--z-border)' }}>
              <td style={{ padding: '8px 10px', fontWeight: 600, whiteSpace: 'nowrap' }}>
                {t.numeroDocumento}
              </td>
              <td style={{ padding: '8px 10px', whiteSpace: 'nowrap' }}>{t.nombre}</td>
              <td style={{ padding: '8px 10px' }}>
                <ChipPerfil perfil={t.perfilSeleccionada} />
              </td>
              <td style={{ padding: '8px 10px' }}>
                <ChipPerfil perfil={t.perfilOtraCartera} />
              </td>
              <td style={{ padding: '8px 10px', whiteSpace: 'nowrap' }}>
                {t.numeroMarcadoSeleccionada ?? '—'}
              </td>
              <td style={{ padding: '8px 10px', whiteSpace: 'nowrap' }}>
                {t.numeroMarcadoOtra ?? '—'}
              </td>
              <td style={{ padding: '8px 10px', whiteSpace: 'nowrap', textAlign: 'right' }}>
                {t.saldoTotal != null ? fmtCOP(t.saldoTotal) : '—'}
              </td>
              <td style={{ padding: '8px 10px', whiteSpace: 'nowrap' }}>
                {fmtFecha(t.fechaUltimaGestion)}
              </td>
              <td style={{ padding: '8px 10px', whiteSpace: 'nowrap', textAlign: 'right' }}>
                {t.valorPromesa != null ? fmtCOP(t.valorPromesa) : '—'}
              </td>
            </tr>
          ))}
        </tbody>
      </table>
    </div>
  );
}

function SeccionCartera({
  seccion,
  carteraSeleccionadaID,
  nombreSeleccionada,
}: {
  seccion: ComparadorVsCarteraDto;
  carteraSeleccionadaID: number;
  nombreSeleccionada: string;
}) {
  const [descargando, setDescargando] = useState(false);

  const handleDescargar = async () => {
    setDescargando(true);
    try {
      await descargarExcelComparativo(carteraSeleccionadaID, seccion.carteraID, nombreSeleccionada, seccion.nombreCartera);
    } finally {
      setDescargando(false);
    }
  };

  return (
    <div style={{
      background: 'var(--z-bg-card)',
      border: '1px solid var(--z-border)',
      borderRadius: 10,
      padding: '20px 24px',
      marginBottom: 16,
    }}>
      {/* Cabecera de sección */}
      <div style={{
        display: 'flex', alignItems: 'center',
        justifyContent: 'space-between', flexWrap: 'wrap', gap: 12,
      }}>
        <div>
          <p style={{
            fontSize: 11, fontWeight: 700, textTransform: 'uppercase',
            letterSpacing: '0.08em', color: 'var(--z-text-muted)', marginBottom: 4,
          }}>
            vs
          </p>
          <h3 style={{ fontSize: 16, fontWeight: 700, margin: 0 }}>
            {seccion.nombreCartera}
          </h3>
          <p style={{ fontSize: 13, color: 'var(--z-text-muted)', marginTop: 4 }}>
            <strong style={{ color: 'var(--z-accent)' }}>{fmt(seccion.totalCoincidencias)}</strong>
            {' '}titular{seccion.totalCoincidencias !== 1 ? 'es' : ''} con estado positivo o pago
            en esta cartera que no tienen gestión en{' '}
            <strong>{nombreSeleccionada}</strong>
          </p>
        </div>

        <button
          onClick={handleDescargar}
          disabled={descargando}
          style={{
            padding: '8px 16px',
            background: descargando ? 'var(--z-border)' : 'var(--z-accent)',
            color: '#fff',
            border: 'none',
            borderRadius: 6,
            cursor: descargando ? 'not-allowed' : 'pointer',
            fontSize: 13,
            fontWeight: 600,
            whiteSpace: 'nowrap',
          }}
        >
          {descargando ? 'Generando...' : `Descargar Excel (${fmt(seccion.totalCoincidencias)})`}
        </button>
      </div>

      {/* Vista previa */}
      {seccion.preview.length > 0 && (
        <>
          <div style={{
            marginTop: 16,
            display: 'flex', alignItems: 'center', gap: 8,
          }}>
            <span style={{ fontSize: 12, color: 'var(--z-text-muted)', fontWeight: 600 }}>
              VISTA PREVIA — {seccion.preview.length} de {fmt(seccion.totalCoincidencias)}
            </span>
            {seccion.totalCoincidencias > 5 && (
              <span style={{
                fontSize: 11, padding: '1px 6px', borderRadius: 4,
                background: 'color-mix(in srgb, var(--z-accent) 15%, transparent)',
                color: 'var(--z-accent)',
              }}>
                +{fmt(seccion.totalCoincidencias - 5)} en el Excel
              </span>
            )}
          </div>
          <TablaTitulares
            titulares={seccion.preview}
            nombreSeleccionada={nombreSeleccionada}
            nombreOtra={seccion.nombreCartera}
          />
        </>
      )}
    </div>
  );
}

// ── Página principal ──────────────────────────────────────────────────────────

export function ComparadorCarterasPage() {
  const [carteraID, setCarteraID] = useState<number | null>(null);
  const [descargandoCompleto, setDescargandoCompleto] = useState(false);

  const handleDescargarCompleto = async () => {
    if (!carteraID) return;
    setDescargandoCompleto(true);
    try {
      await descargarExcelCompleto(carteraID, data!.nombreCarteraSeleccionada);
    } finally {
      setDescargandoCompleto(false);
    }
  };

  const { data: opciones, isLoading: cargandoOpciones } = useQuery({
    queryKey: ['comparador-opciones'],
    queryFn: obtenerOpcionesComparador,
  });

  const { data, isLoading: cargandoDatos, isError } = useQuery({
    queryKey: ['comparador', carteraID],
    queryFn: () => compararCartera(carteraID!),
    enabled: carteraID !== null,
  });

  return (
    <div style={{ maxWidth: 1300, margin: '0 auto' }}>

      {/* Encabezado */}
      <div style={{ display: 'flex', justifyContent: 'space-between', alignItems: 'flex-start', flexWrap: 'wrap', gap: 12, marginBottom: 24 }}>
        <div>
          <h1 style={{ fontSize: 20, fontWeight: 600, color: 'var(--z-text-primary)', margin: 0 }}>
            Comparador de Carteras
          </h1>
          <p style={{ fontSize: 13, color: 'var(--z-text-muted)', margin: '4px 0 0' }}>
            Identifica titulares sin gestión en la cartera seleccionada que ya tienen
            contacto positivo o pago en otra cartera.
          </p>
        </div>
      </div>

      {/* Selector */}
      <div style={{ marginBottom: 28 }}>
        <label style={{ fontSize: 13, fontWeight: 600, display: 'block', marginBottom: 6 }}>
          Cartera a analizar
        </label>
        <select
          value={carteraID ?? ''}
          onChange={e => setCarteraID(e.target.value ? Number(e.target.value) : null)}
          disabled={cargandoOpciones}
          style={{
            padding: '8px 12px', borderRadius: 6,
            border: '1px solid var(--z-border)',
            background: 'var(--z-bg-card)', color: 'var(--z-text)',
            fontSize: 14, minWidth: 300,
          }}
        >
          <option value="">— Selecciona una cartera —</option>
          {opciones?.map(o => (
            <option key={o.carteraID} value={o.carteraID}>
              {o.nombreCartera}
            </option>
          ))}
        </select>
      </div>

      {cargandoDatos && (
        <div style={{ display: 'flex', justifyContent: 'center', marginTop: 48 }}>
          <CargandoSpiral />
        </div>
      )}

      {isError && (
        <div style={{ color: '#EF4444', fontSize: 14 }}>
          No se pudo cargar la información. Intenta de nuevo.
        </div>
      )}

      {data && (
        <>
          {/* Resumen */}
          <div style={{
            background: 'var(--z-bg-card)',
            border: '1px solid var(--z-border)',
            borderRadius: 10,
            padding: '16px 24px',
            marginBottom: 24,
            display: 'flex', gap: 32, flexWrap: 'wrap', alignItems: 'center',
          }}>
            <div>
              <p style={{ fontSize: 11, fontWeight: 700, textTransform: 'uppercase',
                          letterSpacing: '0.08em', color: 'var(--z-text-muted)', marginBottom: 4 }}>
                Cartera seleccionada
              </p>
              <p style={{ fontSize: 18, fontWeight: 700 }}>{data.nombreCarteraSeleccionada}</p>
            </div>
            <div>
              <p style={{ fontSize: 11, fontWeight: 700, textTransform: 'uppercase',
                          letterSpacing: '0.08em', color: 'var(--z-text-muted)', marginBottom: 4 }}>
                Base del comparativo
              </p>
              <p style={{ fontSize: 28, fontWeight: 700, color: '#EF4444' }}>
                {fmt(data.totalSinGestion)}
              </p>
            </div>
            <div>
              <p style={{ fontSize: 11, fontWeight: 700, textTransform: 'uppercase',
                          letterSpacing: '0.08em', color: 'var(--z-text-muted)', marginBottom: 4 }}>
                Carteras con oportunidades
              </p>
              <p style={{ fontSize: 28, fontWeight: 700, color: 'var(--z-accent)' }}>
                {data.comparaciones.length}
              </p>
            </div>

            {data.comparaciones.length > 0 && (
              <div style={{ marginLeft: 'auto' }}>
                <button
                  onClick={handleDescargarCompleto}
                  disabled={descargandoCompleto}
                  style={{
                    padding: '10px 20px',
                    background: descargandoCompleto ? 'var(--z-border)' : '#16A34A',
                    color: '#fff',
                    border: 'none',
                    borderRadius: 6,
                    cursor: descargandoCompleto ? 'not-allowed' : 'pointer',
                    fontSize: 13,
                    fontWeight: 600,
                    whiteSpace: 'nowrap',
                  }}
                >
                  {descargandoCompleto ? 'Generando...' : 'Descargar Excel completo'}
                </button>
                <p style={{ fontSize: 11, color: 'var(--z-text-muted)', marginTop: 4, textAlign: 'center' }}>
                  Todas las carteras combinadas
                </p>
              </div>
            )}
          </div>

          {data.comparaciones.length === 0 ? (
            <div style={{
              background: 'var(--z-bg-card)', border: '1px solid var(--z-border)',
              borderRadius: 10, padding: '32px 24px', textAlign: 'center',
              color: 'var(--z-text-muted)', fontSize: 14,
            }}>
              No se encontraron titulares con mejor estado en otras carteras.
            </div>
          ) : (
            data.comparaciones.map(seccion => (
              <SeccionCartera
                key={seccion.carteraID}
                seccion={seccion}
                carteraSeleccionadaID={data.carteraID}
                nombreSeleccionada={data.nombreCarteraSeleccionada}
              />
            ))
          )}
        </>
      )}
    </div>
  );
}
