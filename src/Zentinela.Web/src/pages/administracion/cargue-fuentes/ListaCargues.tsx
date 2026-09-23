// "Cargues en curso / recientes": une los jobs vivos en memoria del servidor con las
// ejecuciones auditadas de las últimas 48 h. Desde aquí se retoma un cargue tras salir de
// la página (o desde otra pestaña, donde sessionStorage no tiene el jobId).
import { useQuery } from '@tanstack/react-query';
import { listarJobs } from '../../../api/cargueFuentesApi';
import type { CargueJobResumen } from '../../../types/cargueFuentes';
import { TH, TD } from '../historico-virtualidad/compartidos';
import { fechaLegible, num } from './formato';

type Badge = { label: string; color: string; bg: string; borde: string };

const AZUL:  Omit<Badge, 'label'> = { color: '#1d4ed8', bg: '#eff6ff', borde: '#93c5fd' };
const AMBAR: Omit<Badge, 'label'> = { color: '#b45309', bg: '#fefce8', borde: '#fde68a' };
const VERDE: Omit<Badge, 'label'> = { color: '#16a34a', bg: '#f0fdf4', borde: '#86efac' };
const ROJO:  Omit<Badge, 'label'> = { color: '#dc2626', bg: '#fef2f2', borde: '#fca5a5' };
const GRIS:  Omit<Badge, 'label'> = { color: '#64748b', bg: 'var(--z-bg-page)', borde: 'var(--z-border)' };

// El estado llega como texto libre: del job en memoria (Validando, PreviewListo, …) o de la
// tabla de auditoría (EN CURSO, OK, ERROR, CANCELADA). Se normaliza a una etiqueta legible.
function badgeDe(r: CargueJobResumen): Badge {
  const e = r.estado.trim().toUpperCase();
  // Un cargue "en curso" que ya no está en memoria murió con un reinicio del servidor (la
  // transacción se revirtió sola); el backend además lo marca como error por reinicio.
  const enCurso = e === 'VALIDANDO' || e === 'CARGANDO' || e === 'EN CURSO';
  if (!r.enMemoria && (enCurso || e.includes('INTERRUMP') || e.includes('REINICI')))
    return { label: 'Interrumpido', ...AMBAR };
  if (e === 'VALIDANDO')    return { label: 'Validando', ...AZUL };
  if (e === 'CARGANDO' || e === 'EN CURSO') return { label: 'Cargando', ...AZUL };
  if (e === 'PREVIEWLISTO') return { label: 'Preview listo', ...AMBAR };
  if (e === 'COMPLETADO' || e === 'OK') return { label: 'Completado', ...VERDE };
  if (e === 'ERROR')        return { label: 'Error', ...ROJO };
  if (e.startsWith('CANCELAD')) return { label: 'Cancelado', ...GRIS };
  return { label: r.estado, ...GRIS };
}

function BadgeEstado({ r }: { r: CargueJobResumen }) {
  const b = badgeDe(r);
  return (
    <span style={{
      display: 'inline-block', padding: '2px 8px', borderRadius: 99, fontSize: 11, fontWeight: 600,
      color: b.color, background: b.bg, border: `1px solid ${b.borde}`, whiteSpace: 'nowrap',
    }}>
      {b.label}
    </span>
  );
}

export function ListaCargues({ jobActual, onRetomar }: {
  jobActual: string | null;
  onRetomar: (jobId: string) => void;
}) {
  const { data: cargues = [], isLoading, isError } = useQuery({
    queryKey: ['cargue-fuentes-jobs'],
    queryFn:  listarJobs,
    refetchInterval: 10_000,
  });

  return (
    <div style={{ display: 'flex', flexDirection: 'column', gap: 8 }}>
      <p style={{ fontSize: 13, fontWeight: 600, color: 'var(--z-text-primary)', margin: 0 }}>
        Cargues en curso y recientes <span style={{ fontWeight: 400, color: 'var(--z-text-muted)' }}>(últimas 48 horas)</span>
      </p>

      {isError && (
        <p style={{ fontSize: 12, color: '#dc2626', margin: 0 }}>No se pudo consultar la lista de cargues.</p>
      )}
      {!isLoading && !isError && cargues.length === 0 && (
        <p style={{ fontSize: 12, color: 'var(--z-text-muted)', margin: 0 }}>No hay cargues en las últimas 48 horas.</p>
      )}

      {cargues.length > 0 && (
        <div style={{ overflow: 'auto', maxHeight: 280, border: '1px solid var(--z-border)', borderRadius: 8 }}>
          <table style={{ width: '100%', borderCollapse: 'collapse' }}>
            <thead style={{ position: 'sticky', top: 0, background: 'var(--z-bg-card)', zIndex: 1 }}>
              <tr>
                <th style={TH}>Fuente</th>
                <th style={TH}>Usuario</th>
                <th style={TH}>Archivo</th>
                <th style={TH}>Fecha</th>
                <th style={TH}>Estado</th>
                <th style={{ ...TH, textAlign: 'right' }}>%</th>
                <th style={{ ...TH, textAlign: 'right' }}>Insertadas / refrescadas</th>
                <th style={TH} />
              </tr>
            </thead>
            <tbody>
              {cargues.map((r, i) => {
                const actual = !!r.jobId && r.jobId === jobActual;
                return (
                  <tr key={r.jobId ?? `ej-${r.ejecucionID ?? i}`} style={{ background: actual ? 'var(--z-brand-light)' : 'transparent' }}>
                    <td style={{ ...TD, fontWeight: 600 }}>{r.fuente}</td>
                    <td style={TD}>{r.usuario}</td>
                    <td style={{ ...TD, maxWidth: 240, overflow: 'hidden', textOverflow: 'ellipsis' }} title={r.nombreArchivo}>
                      {r.nombreArchivo}
                    </td>
                    <td style={{ ...TD, color: 'var(--z-text-muted)' }}>{fechaLegible(r.creadoEn)}</td>
                    <td style={TD}><BadgeEstado r={r} /></td>
                    <td style={{ ...TD, textAlign: 'right', fontVariantNumeric: 'tabular-nums' }}>{Math.round(r.porcentaje)}%</td>
                    <td style={{ ...TD, textAlign: 'right', fontVariantNumeric: 'tabular-nums', color: 'var(--z-text-muted)' }}>
                      {r.insertadas != null || r.refrescadas != null
                        ? `${num(r.insertadas ?? 0)} / ${num(r.refrescadas ?? 0)}`
                        : '—'}
                    </td>
                    <td style={{ ...TD, textAlign: 'right' }}>
                      {/* Solo se puede retomar lo que sigue vivo en memoria del servidor. */}
                      {r.enMemoria && r.jobId && !actual && (
                        <button
                          onClick={() => onRetomar(r.jobId!)}
                          style={{
                            padding: '4px 12px', borderRadius: 6, border: '0.5px solid var(--z-brand)',
                            background: 'transparent', color: 'var(--z-brand)', fontSize: 12, fontWeight: 600, cursor: 'pointer',
                          }}
                        >
                          Retomar
                        </button>
                      )}
                      {actual && <span style={{ fontSize: 11, color: 'var(--z-brand)', fontWeight: 600 }}>En pantalla</span>}
                    </td>
                  </tr>
                );
              })}
            </tbody>
          </table>
        </div>
      )}
    </div>
  );
}
