import { useQuery } from '@tanstack/react-query';
import { useNavigate } from 'react-router-dom';
import { useAuth } from '../../context/AuthContext';
import { obtenerDashboardAsesor } from '../../api/dashboardApi';
import { CargandoSpiral } from '../../components/CargandoSpiral';
import type { AcuerdoVigenteDto, PagosConfirmadosDto } from '../../types/dashboard';

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
      <td style={{ padding: '10px 14px', fontSize: '13px', color: 'var(--z-text-muted)', textAlign: 'right', borderBottom: `0.5px solid ${color.border}` }}>
        {formatearMoneda(acuerdo.saldoTotal)}
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

function TarjetaMetrica({ label, valor, sub, valorSize = 28 }: { label: string; valor: string; sub?: string; valorSize?: number }) {
  return (
    <div style={{
      background: 'var(--z-bg-card)',
      border: '0.5px solid var(--z-border)',
      borderRadius: '8px',
      padding: '20px',
      flex: '1 1 140px',
      minWidth: '140px',
    }}>
      <p style={{ fontSize: '12px', color: 'var(--z-text-muted)', marginBottom: '8px' }}>{label}</p>
      <p style={{ fontSize: `${valorSize}px`, fontWeight: 600, color: 'var(--z-text-primary)', lineHeight: 1, wordBreak: 'break-word' }}>{valor}</p>
      {sub && <p style={{ fontSize: '11px', color: 'var(--z-text-muted)', marginTop: '6px' }}>{sub}</p>}
    </div>
  );
}

// ── Ring de honorarios ────────────────────────────────────────────────────────

function RingHonorarios({ actual, meta }: { actual: number; meta: number }) {
  const r = 40;
  const circum = 2 * Math.PI * r;
  const pct = meta > 0 ? Math.min(actual / meta, 1) : 0;
  const dash = pct * circum;
  const pctLabel = meta > 0 ? `${(pct * 100).toFixed(0)}%` : '—';

  return (
    <div style={{ display: 'flex', flexDirection: 'column', alignItems: 'center', gap: '8px' }}>
      <svg viewBox="0 0 96 96" width="140" height="140">
        {/* Fondo */}
        <circle cx="48" cy="48" r={r} fill="none" stroke="var(--z-border)" strokeWidth="8" />
        {/* Progreso */}
        <circle
          cx="48" cy="48" r={r} fill="none"
          stroke="var(--z-brand)"
          strokeWidth="8"
          strokeDasharray={`${dash} ${circum}`}
          strokeLinecap="round"
          transform="rotate(-90 48 48)"
          style={{ transition: 'stroke-dasharray 0.4s ease' }}
        />
        <text x="48" y="43" textAnchor="middle" fontSize="14" fontWeight="700"
          fill="var(--z-text-primary)" fontFamily="inherit">
          {pctLabel}
        </text>
        <text x="48" y="57" textAnchor="middle" fontSize="9"
          fill="var(--z-text-muted)" fontFamily="inherit">
          de meta
        </text>
      </svg>
      <p style={{ fontSize: '11px', color: 'var(--z-text-muted)', textAlign: 'center', lineHeight: 1.4 }}>
        Honorarios<br />
        <span style={{ fontWeight: 600, color: 'var(--z-text-primary)' }}>
          {formatearMoneda(actual)}
        </span>
        {' / '}{formatearMoneda(meta)}
      </p>
    </div>
  );
}

// ── Sección Pagos Confirmados ─────────────────────────────────────────────────

function SeccionPagosConfirmados({ pagos, isLoading }: { pagos?: PagosConfirmadosDto; isLoading: boolean }) {
  const navigate = useNavigate();
  return (
    <section>
      <div style={{ display: 'flex', alignItems: 'baseline', justifyContent: 'space-between', marginBottom: '12px' }}>
        <div>
          <h2 style={{ fontSize: '13px', fontWeight: 600, color: 'var(--z-text-primary)', marginBottom: '2px' }}>
            Pagos confirmados
          </h2>
          <p style={{ fontSize: '12px', color: 'var(--z-text-muted)' }}>
            Actualizado última vez:{' '}
            <span style={{ fontWeight: 500, color: 'var(--z-text-primary)' }}>
              {isLoading ? '...' : formatearFechaActualizacion(pagos?.fechaActualizacion ?? null)}
            </span>
          </p>
        </div>
        <button
          onClick={() => navigate('/home/pagos')}
          style={{
            background: 'none',
            border: '0.5px solid var(--z-border)',
            borderRadius: '6px',
            padding: '5px 12px',
            fontSize: '12px',
            color: 'var(--z-brand)',
            cursor: 'pointer',
            fontWeight: 500,
          }}
        >
          Ver detalles →
        </button>
      </div>

      {!isLoading && pagos && (
        <div style={{
          background: 'var(--z-bg-card)',
          border: '0.5px solid var(--z-border)',
          borderRadius: '8px',
          padding: '24px',
          display: 'flex',
          gap: '24px',
          alignItems: 'center',
          flexWrap: 'wrap',
        }}>
          {/* Ring principal */}
          <RingHonorarios actual={pagos.totalHonorarios} meta={pagos.metaHonorarios} />

          {/* Separador */}
          <div style={{ width: '1px', background: 'var(--z-border)', alignSelf: 'stretch', minHeight: '80px' }} />

          {/* Tarjetas métricas */}
          <div style={{ display: 'flex', gap: '12px', flexWrap: 'wrap', flex: 1 }}>
            <TarjetaMetrica
              label="Recaudo"
              valor={formatearMoneda(pagos.totalRecaudo)}
              valorSize={18}
            />
            <TarjetaMetrica
              label="Honorarios"
              valor={formatearMoneda(pagos.totalHonorarios)}
              valorSize={18}
            />
            <TarjetaMetrica
              label="Efectividad / gestiones"
              valor={`${(pagos.porcentajeEfectividad * 100).toFixed(1)}%`}
              sub="Acuerdos y abonos / Total gestiones"
            />
          </div>
        </div>
      )}

      {!isLoading && !pagos && (
        <div style={{
          background: 'var(--z-bg-card)',
          border: '0.5px solid var(--z-border)',
          borderRadius: '8px',
          padding: '32px',
          textAlign: 'center',
          fontSize: '13px',
          color: 'var(--z-text-muted)',
        }}>
          Sin datos de pagos disponibles.
        </div>
      )}
    </section>
  );
}

// ── Componente principal ──────────────────────────────────────────────────────

export function DashboardAsesorPage() {
  const { usuario } = useAuth();
  const { data, isLoading, isError, error } = useQuery({
    queryKey: ['dashboard-asesor'],
    queryFn: obtenerDashboardAsesor,
    staleTime: 5 * 60 * 1000,
    retry: false,
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
          {(error as any)?.response?.data?.mensaje ?? 'No se pudo cargar el dashboard. Intenta de nuevo.'}
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
            <CargandoSpiral mensaje="Cargando acuerdos..." />
          ) : !data || data.acuerdosVigentes.length === 0 ? (
            <div style={{ padding: '40px', textAlign: 'center', fontSize: '13px', color: 'var(--z-text-muted)' }}>
              No tienes acuerdos de pago vigentes.
            </div>
          ) : (
            <table style={{ width: '100%', borderCollapse: 'collapse' }}>
              <thead>
                <tr style={{ background: 'var(--z-bg-page)' }}>
                  {['Titular', 'Tipificación', 'Fecha promesa', 'Promesa', 'Saldo', 'Estado'].map(col => (
                    <th key={col} style={{
                      padding: '10px 14px', textAlign: (col === 'Promesa' || col === 'Saldo') ? 'right' : col === 'Estado' ? 'center' : 'left',
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
          <div style={{ display: 'flex', gap: '12px', flexWrap: 'wrap' }}>
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

      {/* ── Pagos confirmados ─────────────────────────────────────────────── */}
      <SeccionPagosConfirmados pagos={data?.pagosConfirmados} isLoading={isLoading} />

    </div>
  );
}
