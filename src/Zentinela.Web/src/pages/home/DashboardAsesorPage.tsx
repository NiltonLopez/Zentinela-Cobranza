import { useQuery } from '@tanstack/react-query';
import { useAuth } from '../../context/AuthContext';
import { obtenerDashboardAsesor } from '../../api/dashboardApi';
import type { AcuerdoVigenteDto } from '../../types/dashboard';

// ── Helpers de formato ────────────────────────────────────────────────────────

function formatearFechaActualizacion(iso: string | null): string {
  if (!iso) return 'Sin datos';
  const fecha = new Date(iso);
  const ahora = new Date();
  const esHoy = fecha.toDateString() === ahora.toDateString();

  if (esHoy) {
    const diffMs = ahora.getTime() - fecha.getTime();
    const diffMin = Math.floor(diffMs / 60000);
    if (diffMin < 60) return `hace ${diffMin} minuto${diffMin !== 1 ? 's' : ''}`;
    const diffHoras = Math.floor(diffMin / 60);
    return `hace ${diffHoras} hora${diffHoras !== 1 ? 's' : ''}`;
  }

  return fecha.toLocaleDateString('es-CO', { day: 'numeric', month: 'long', year: 'numeric' });
}

function formatearPorcentaje(valor: number): string {
  return `${(valor * 100).toFixed(1)}%`;
}

function formatearMoneda(valor: number | null): string {
  if (valor === null) return '—';
  return new Intl.NumberFormat('es-CO', { style: 'currency', currency: 'COP', maximumFractionDigits: 0 }).format(valor);
}

// ── Colores de semáforo ───────────────────────────────────────────────────────

const SEMAFORO: Record<string, { bg: string; border: string; badge: string; label: string }> = {
  rojo:     { bg: '#fff5f5', border: '#fecaca', badge: '#dc2626', label: 'Vencido' },
  amarillo: { bg: '#fffbeb', border: '#fde68a', badge: '#d97706', label: 'Próximo' },
  verde:    { bg: '#f0fdf4', border: '#bbf7d0', badge: '#16a34a', label: 'A tiempo' },
};

// ── Sub-componentes ───────────────────────────────────────────────────────────

function FilaAcuerdo({ acuerdo }: { acuerdo: AcuerdoVigenteDto }) {
  const color = SEMAFORO[acuerdo.semaforo] ?? SEMAFORO.verde;
  return (
    <tr style={{ background: color.bg }}>
      <td style={{ padding: '10px 14px', fontSize: '13px', color: 'var(--z-text-primary)', borderBottom: `0.5px solid ${color.border}` }}>
        {acuerdo.nombreTitular}
        <span style={{ fontSize: '11px', color: 'var(--z-text-muted)', marginLeft: '6px' }}>
          {acuerdo.titularID}
        </span>
      </td>
      <td style={{ padding: '10px 14px', fontSize: '13px', color: 'var(--z-text-primary)', borderBottom: `0.5px solid ${color.border}` }}>
        {acuerdo.ultimoPerfil}
      </td>
      <td style={{ padding: '10px 14px', fontSize: '13px', color: 'var(--z-text-primary)', borderBottom: `0.5px solid ${color.border}` }}>
        {new Date(acuerdo.fechaPromesa + 'T00:00:00').toLocaleDateString('es-CO', { day: 'numeric', month: 'short', year: 'numeric' })}
      </td>
      <td style={{ padding: '10px 14px', fontSize: '13px', color: 'var(--z-text-primary)', textAlign: 'right', borderBottom: `0.5px solid ${color.border}` }}>
        {formatearMoneda(acuerdo.valorPromesa)}
      </td>
      <td style={{ padding: '10px 14px', textAlign: 'center', borderBottom: `0.5px solid ${color.border}` }}>
        <span style={{
          display: 'inline-block',
          padding: '2px 8px',
          borderRadius: '100px',
          fontSize: '11px',
          fontWeight: 600,
          background: color.badge,
          color: '#fff',
        }}>
          {color.label}
        </span>
      </td>
    </tr>
  );
}

function TarjetaMetrica({ label, valor, sub }: { label: string; valor: string; sub?: string }) {
  return (
    <div style={{
      background: 'var(--z-bg-card)',
      border: '0.5px solid var(--z-border)',
      borderRadius: '8px',
      padding: '20px',
      flex: 1,
    }}>
      <p style={{ fontSize: '12px', color: 'var(--z-text-muted)', marginBottom: '8px' }}>{label}</p>
      <p style={{ fontSize: '28px', fontWeight: 600, color: 'var(--z-text-primary)', lineHeight: 1 }}>{valor}</p>
      {sub && <p style={{ fontSize: '11px', color: 'var(--z-text-muted)', marginTop: '6px' }}>{sub}</p>}
    </div>
  );
}

// ── Componente principal ──────────────────────────────────────────────────────

export function DashboardAsesorPage() {
  const { usuario } = useAuth();
  const { data, isLoading, isError } = useQuery({
    queryKey: ['dashboard-asesor'],
    queryFn: obtenerDashboardAsesor,
    staleTime: 5 * 60 * 1000, // 5 minutos
  });

  return (
    <div style={{ display: 'flex', flexDirection: 'column', gap: '28px', maxWidth: '960px' }}>

      {/* Encabezado */}
      <div>
        <h1 style={{ fontSize: '16px', fontWeight: 500, color: 'var(--z-text-primary)', marginBottom: '4px' }}>
          Hola, {usuario?.nombreCompleto}
        </h1>
        <p style={{ fontSize: '13px', color: 'var(--z-text-muted)' }}>
          {isLoading ? 'Cargando...'
            : data?.nombreSupervisor
              ? `Supervisor: ${data.nombreSupervisor}`
              : 'Sin supervisor asignado'}
        </p>
      </div>

      {isError && (
        <div style={{
          background: '#fff5f5', border: '0.5px solid #fecaca',
          borderRadius: '8px', padding: '14px 16px',
          fontSize: '13px', color: '#dc2626',
        }}>
          No se pudo cargar el dashboard. Intenta de nuevo.
        </div>
      )}

      {/* ── Acuerdos vigentes ─────────────────────────────────────────────── */}
      <section>
        <div style={{ display: 'flex', alignItems: 'baseline', gap: '10px', marginBottom: '12px' }}>
          <h2 style={{ fontSize: '13px', fontWeight: 600, color: 'var(--z-text-primary)' }}>
            Acuerdos de pago vigentes
          </h2>
          {data && (
            <span style={{ fontSize: '12px', color: 'var(--z-text-muted)' }}>
              {data.acuerdosVigentes.length} acuerdo{data.acuerdosVigentes.length !== 1 ? 's' : ''}
            </span>
          )}
        </div>

        <div style={{
          background: 'var(--z-bg-card)',
          border: '0.5px solid var(--z-border)',
          borderRadius: '8px',
          overflow: 'hidden',
        }}>
          {isLoading ? (
            <div style={{ padding: '40px', textAlign: 'center', fontSize: '13px', color: 'var(--z-text-muted)' }}>
              Cargando acuerdos...
            </div>
          ) : !data || data.acuerdosVigentes.length === 0 ? (
            <div style={{ padding: '40px', textAlign: 'center', fontSize: '13px', color: 'var(--z-text-muted)' }}>
              No tienes acuerdos de pago vigentes.
            </div>
          ) : (
            <table style={{ width: '100%', borderCollapse: 'collapse' }}>
              <thead>
                <tr style={{ background: 'var(--z-bg-page)' }}>
                  {['Titular', 'Tipificación', 'Fecha promesa', 'Valor', 'Estado'].map(col => (
                    <th key={col} style={{
                      padding: '10px 14px', textAlign: col === 'Valor' ? 'right' : col === 'Estado' ? 'center' : 'left',
                      fontSize: '11px', fontWeight: 600,
                      color: 'var(--z-text-muted)',
                      borderBottom: '0.5px solid var(--z-border)',
                      letterSpacing: '0.04em',
                      textTransform: 'uppercase',
                    }}>
                      {col}
                    </th>
                  ))}
                </tr>
              </thead>
              <tbody>
                {data.acuerdosVigentes.map(a => (
                  <FilaAcuerdo key={`${a.titularID}`} acuerdo={a} />
                ))}
              </tbody>
            </table>
          )}
        </div>
      </section>

      {/* ── Rendimiento ───────────────────────────────────────────────────── */}
      <section>
        <div style={{ marginBottom: '12px' }}>
          <h2 style={{ fontSize: '13px', fontWeight: 600, color: 'var(--z-text-primary)', marginBottom: '2px' }}>
            Rendimiento
          </h2>
          <p style={{ fontSize: '12px', color: 'var(--z-text-muted)' }}>
            Actualizado última vez:{' '}
            <span style={{ fontWeight: 500, color: 'var(--z-text-primary)' }}>
              {isLoading ? '...' : formatearFechaActualizacion(data?.rendimiento.fechaActualizacion ?? null)}
            </span>
          </p>
        </div>

        {!isLoading && data && (
          <div style={{ display: 'flex', gap: '12px' }}>
            <TarjetaMetrica
              label="Contactabilidad"
              valor={formatearPorcentaje(data.rendimiento.tasaContactabilidad)}
              sub="Contactos positivos / Total gestiones"
            />
            <TarjetaMetrica
              label="Efectividad"
              valor={formatearPorcentaje(data.rendimiento.tasaEfectividad)}
              sub="Acuerdos y abonos / Contactos positivos"
            />
            <TarjetaMetrica
              label="Productividad"
              valor={String(data.rendimiento.totalGestiones)}
              sub={`Gestiones del ${data.rendimiento.fechaActualizacion
                ? new Date(data.rendimiento.fechaActualizacion).toLocaleDateString('es-CO', { day: 'numeric', month: 'short' })
                : 'día'}`}
            />
          </div>
        )}

        {!isLoading && (!data || data.rendimiento.fechaActualizacion === null) && (
          <div style={{
            background: 'var(--z-bg-card)',
            border: '0.5px solid var(--z-border)',
            borderRadius: '8px',
            padding: '32px',
            textAlign: 'center',
            fontSize: '13px',
            color: 'var(--z-text-muted)',
          }}>
            Sin datos de rendimiento disponibles.
          </div>
        )}
      </section>

    </div>
  );
}
