import { useState } from 'react';
import { useQuery } from '@tanstack/react-query';
import {
  obtenerDashboardLider,
  obtenerCarterasLiderPropias,
} from '../../api/dashboardApi';
import type { FilaAsesorDto } from '../../types/dashboard';
import { useAuth } from '../../context/AuthContext';
import { CargandoSpiral } from '../../components/CargandoSpiral';
import { ThOrdenable } from '../../components/ThOrdenable';
import { useSort } from '../../hooks/useSort';

// ── Tipos ─────────────────────────────────────────────────────────────────────

type Preset = 'ultimo' | 'hoy' | 'ciclo' | 'mes-pasado' | '7d' | '30d' | 'custom';

interface Rango { desde: string; hasta: string; }

// ── Helpers ───────────────────────────────────────────────────────────────────

function isoHoy(): string {
  return new Date().toISOString().slice(0, 10);
}

function isoFecha(d: Date): string {
  return d.toISOString().slice(0, 10);
}

function calcularRango(preset: Preset, customDesde: string, customHasta: string): Rango | null {
  if (preset === 'ultimo') return null;
  const hoy = new Date();
  switch (preset) {
    case 'hoy':
      return { desde: isoHoy(), hasta: isoHoy() };
    case 'ciclo': {
      const inicio = new Date(hoy.getFullYear(), hoy.getMonth(), 1);
      return { desde: isoFecha(inicio), hasta: isoHoy() };
    }
    case 'mes-pasado': {
      const inicio = new Date(hoy.getFullYear(), hoy.getMonth() - 1, 1);
      const fin    = new Date(hoy.getFullYear(), hoy.getMonth(), 0);
      return { desde: isoFecha(inicio), hasta: isoFecha(fin) };
    }
    case '7d': {
      const inicio = new Date(hoy);
      inicio.setDate(hoy.getDate() - 6);
      return { desde: isoFecha(inicio), hasta: isoHoy() };
    }
    case '30d': {
      const inicio = new Date(hoy);
      inicio.setDate(hoy.getDate() - 29);
      return { desde: isoFecha(inicio), hasta: isoHoy() };
    }
    case 'custom':
      return { desde: customDesde || isoHoy(), hasta: customHasta || isoHoy() };
  }
}

function pct(valor: number): string {
  return `${(valor * 100).toFixed(1)}%`;
}

function formatearPesos(valor: number): string {
  return valor.toLocaleString('es-CO', { style: 'currency', currency: 'COP', maximumFractionDigits: 0 });
}

function formatearFechaCorta(iso: string): string {
  const [y, m, d] = iso.split('-');
  return `${d}/${m}/${y}`;
}

function formatearFechaHora(iso: string | null): string {
  if (!iso) return 'Sin datos';
  const fecha = new Date(iso);
  return fecha.toLocaleDateString('es-CO', {
    day: 'numeric', month: 'long', year: 'numeric',
    hour: '2-digit', minute: '2-digit',
  });
}

// ── Presets ───────────────────────────────────────────────────────────────────

const PRESETS: { valor: Preset; label: string }[] = [
  { valor: 'ultimo',     label: 'Hasta cierre' },
  { valor: 'hoy',        label: 'Hoy' },
  { valor: 'ciclo',      label: 'Este mes' },
  { valor: 'mes-pasado', label: 'Mes pasado' },
  { valor: '7d',         label: 'Últimos 7 días' },
  { valor: '30d',        label: 'Últimos 30 días' },
  { valor: 'custom',     label: 'Personalizado' },
];

// ── Sub-componentes ───────────────────────────────────────────────────────────

function TarjetaMetrica({ label, valor, sub, valorSize = 26 }: {
  label: string; valor: string; sub?: string; valorSize?: number;
}) {
  return (
    <div style={{
      background: 'var(--z-bg-card)',
      border: '0.5px solid var(--z-border)',
      borderRadius: '8px',
      padding: '20px 22px',
      flex: '1 1 160px',
      minWidth: '160px',
    }}>
      <p style={{ fontSize: '11px', fontWeight: 600, color: 'var(--z-text-muted)', textTransform: 'uppercase', letterSpacing: '0.04em', marginBottom: '10px' }}>
        {label}
      </p>
      <p style={{ fontSize: `${valorSize}px`, fontWeight: 700, color: 'var(--z-text-primary)', lineHeight: 1, wordBreak: 'break-word' }}>
        {valor}
      </p>
      {sub && (
        <p style={{ fontSize: '11px', color: 'var(--z-text-muted)', marginTop: '8px', lineHeight: 1.4 }}>{sub}</p>
      )}
    </div>
  );
}

function TarjetaAvance({ gestionados, asignados }: { gestionados: number; asignados: number }) {
  const pctNum  = asignados > 0 ? Math.min((gestionados / asignados) * 100, 100) : 0;
  const radio   = 38;
  const grosor  = 7;
  const circunf = 2 * Math.PI * radio;
  const offset  = circunf * (1 - pctNum / 100);
  const color   = pctNum >= 80 ? '#22c55e' : pctNum >= 50 ? '#f59e0b' : '#3b82f6';

  return (
    <div style={{
      background: 'var(--z-bg-card)',
      border: '0.5px solid var(--z-border)',
      borderRadius: '8px',
      padding: '20px 22px',
      flex: '1 1 260px',
      minWidth: '260px',
      display: 'flex',
      alignItems: 'center',
      gap: '20px',
    }}>
      <svg width="96" height="96" viewBox="0 0 96 96" style={{ flexShrink: 0 }}>
        <circle cx="48" cy="48" r={radio} fill="none" stroke="var(--z-border)" strokeWidth={grosor} />
        <circle
          cx="48" cy="48" r={radio}
          fill="none" stroke={color} strokeWidth={grosor}
          strokeLinecap="round"
          strokeDasharray={circunf} strokeDashoffset={offset}
          transform="rotate(-90 48 48)"
          style={{ transition: 'stroke-dashoffset 0.6s ease' }}
        />
        <text x="48" y="44" textAnchor="middle" dominantBaseline="middle"
          style={{ fontSize: '15px', fontWeight: 700, fill: 'var(--z-text-primary)', fontFamily: 'inherit' }}>
          {pctNum.toFixed(1)}%
        </text>
        <text x="48" y="60" textAnchor="middle" dominantBaseline="middle"
          style={{ fontSize: '9px', fill: 'var(--z-text-muted)', fontFamily: 'inherit' }}>
          avance
        </text>
      </svg>

      <div style={{ minWidth: 0 }}>
        <p style={{ fontSize: '11px', fontWeight: 600, color: 'var(--z-text-muted)', textTransform: 'uppercase', letterSpacing: '0.04em', marginBottom: '10px' }}>
          Avance de gestión
        </p>
        <p style={{ fontSize: '13px', color: 'var(--z-text-primary)', marginBottom: '4px' }}>
          <span style={{ fontWeight: 700 }}>{gestionados.toLocaleString('es-CO')}</span>
          <span style={{ color: 'var(--z-text-muted)' }}> gestionados</span>
        </p>
        <p style={{ fontSize: '13px', color: 'var(--z-text-muted)' }}>
          de {asignados.toLocaleString('es-CO')} asignados
        </p>
        <div style={{ marginTop: '10px', height: 4, borderRadius: 99, background: 'var(--z-border)', overflow: 'hidden' }}>
          <div style={{ height: '100%', width: `${pctNum}%`, background: color, borderRadius: 99, transition: 'width 0.6s ease' }} />
        </div>
      </div>
    </div>
  );
}

function TablaAsesores({ filas }: { filas: FilaAsesorDto[] }) {
  const { items, sort, toggle } = useSort<FilaAsesorDto>(
    filas,
    { key: 'totalGestiones', dir: 'desc' },
    (f, k) => (f as unknown as Record<string, string | number>)[k],
  );

  if (filas.length === 0) {
    return (
      <div style={{ padding: '32px', textAlign: 'center', fontSize: '13px', color: 'var(--z-text-muted)' }}>
        Sin datos de asesores para este período.
      </div>
    );
  }

  return (
    <table style={{ width: '100%', borderCollapse: 'collapse' }}>
      <thead>
        <tr style={{ background: 'var(--z-bg-page)' }}>
          <ThOrdenable label="Asesor"          sortKey="nombreAsesor"        sort={sort} onSort={toggle} />
          <ThOrdenable label="Gestiones"       sortKey="totalGestiones"      sort={sort} onSort={toggle} align="right" />
          <ThOrdenable label="Contactabilidad" sortKey="tasaContactabilidad" sort={sort} onSort={toggle} align="right" />
          <ThOrdenable label="Efectividad"     sortKey="tasaEfectividad"     sort={sort} onSort={toggle} align="right" />
          <ThOrdenable label="Proyección"      sortKey="proyeccionPromesas"  sort={sort} onSort={toggle} align="right" />
        </tr>
      </thead>
      <tbody>
        {items.map(f => (
          <tr key={f.agenteID} style={{ borderBottom: '0.5px solid var(--z-border)' }}>
            <td style={{ padding: '11px 14px', fontSize: '13px', color: 'var(--z-text-primary)', fontWeight: 500 }}>
              {f.nombreAsesor}
              <span style={{ fontSize: '11px', color: 'var(--z-text-muted)', marginLeft: '6px', fontWeight: 400 }}>
                #{f.agenteID}
              </span>
            </td>
            <td style={{ padding: '11px 14px', fontSize: '13px', color: 'var(--z-text-primary)', textAlign: 'right' }}>
              {f.totalGestiones.toLocaleString('es-CO')}
            </td>
            <td style={{ padding: '11px 14px', fontSize: '13px', color: 'var(--z-text-primary)', textAlign: 'right' }}>
              {pct(f.tasaContactabilidad)}
            </td>
            <td style={{ padding: '11px 14px', fontSize: '13px', color: 'var(--z-text-primary)', textAlign: 'right' }}>
              {pct(f.tasaEfectividad)}
            </td>
            <td style={{ padding: '11px 14px', fontSize: '13px', color: 'var(--z-text-primary)', textAlign: 'right', fontWeight: 500 }}>
              {formatearPesos(f.proyeccionPromesas)}
            </td>
          </tr>
        ))}
      </tbody>
    </table>
  );
}

// ── Página principal ──────────────────────────────────────────────────────────

export function DashboardLiderPage() {
  const { usuario } = useAuth();
  const liderID = usuario?.liderID ?? null;

  const [carteraID,     setCarteraID]     = useState<number | null>(null);
  const [carteraNombre, setCarteraNombre] = useState<string>('');

  const [preset,      setPreset]      = useState<Preset>('ultimo');
  const [customDesde, setCustomDesde] = useState<string>('');
  const [customHasta, setCustomHasta] = useState<string>('');

  const rango = calcularRango(preset, customDesde, customHasta);

  const { data: carteras = [] } = useQuery({
    queryKey: ['lider-carteras-propias'],
    queryFn:  obtenerCarterasLiderPropias,
    enabled:  liderID !== null,
    staleTime: 10 * 60 * 1000,
  });

  const { data, isLoading, isError } = useQuery({
    queryKey: ['dashboard-lider', carteraID, rango?.desde ?? null, rango?.hasta ?? null],
    queryFn:  () => obtenerDashboardLider(carteraID, rango?.desde, rango?.hasta),
    enabled:  liderID !== null,
    staleTime: 3 * 60 * 1000,
  });

  const tituloFiltro = carteraNombre || 'Todas mis carteras';

  const handleCambiarCartera = (e: React.ChangeEvent<HTMLSelectElement>) => {
    const val = Number(e.target.value);
    if (!val) {
      setCarteraID(null);
      setCarteraNombre('');
    } else {
      const cartera = carteras.find(c => c.id === val);
      setCarteraID(val);
      setCarteraNombre(cartera?.nombreCartera ?? '');
    }
  };

  const estiloBtnPreset = (activo: boolean): React.CSSProperties => ({
    padding: '5px 12px',
    borderRadius: '20px',
    border: '0.5px solid var(--z-border)',
    background: activo ? 'var(--z-text-primary)' : 'var(--z-bg-page)',
    color: activo ? 'var(--z-bg-page)' : 'var(--z-text-muted)',
    fontSize: '12px',
    fontWeight: activo ? 600 : 400,
    cursor: 'pointer',
    whiteSpace: 'nowrap',
  });

  if (!liderID) {
    return (
      <div style={{ padding: '48px', textAlign: 'center', fontSize: '13px', color: 'var(--z-text-muted)' }}>
        Tu cuenta no tiene un líder asociado.
      </div>
    );
  }

  return (
    <div style={{ display: 'flex', flexDirection: 'column', gap: '24px', maxWidth: '1000px' }}>

      {/* Encabezado */}
      <div style={{ display: 'flex', justifyContent: 'space-between', alignItems: 'flex-start', flexWrap: 'wrap', gap: 12 }}>
        <div>
          <h1 style={{ fontSize: 20, fontWeight: 600, color: 'var(--z-text-primary)', margin: 0 }}>
            Dashboard Líder
          </h1>
          <p style={{ fontSize: 13, color: 'var(--z-text-muted)', margin: '4px 0 0' }}>
            Rendimiento del equipo &mdash; <span style={{ fontWeight: 500, color: 'var(--z-text-primary)' }}>{tituloFiltro}</span>
          </p>
        </div>
      </div>

      {/* Filtros */}
      <div style={{
        background: 'var(--z-bg-card)', border: '0.5px solid var(--z-border)',
        borderRadius: '8px', padding: '16px 20px',
        display: 'flex', flexDirection: 'column', gap: '14px',
      }}>
        {/* Cartera */}
        <div style={{ display: 'flex', gap: '12px', alignItems: 'flex-end', flexWrap: 'wrap' }}>
          <div style={{ display: 'flex', flexDirection: 'column', gap: '4px', minWidth: '220px' }}>
            <label style={{ fontSize: '11px', fontWeight: 600, color: 'var(--z-text-muted)', textTransform: 'uppercase', letterSpacing: '0.04em' }}>
              Cartera
            </label>
            <select
              value={carteraID ?? ''}
              onChange={handleCambiarCartera}
              style={{
                padding: '7px 10px', fontSize: '13px', borderRadius: '6px',
                border: '0.5px solid var(--z-border)', background: 'var(--z-bg-page)',
                color: 'var(--z-text-primary)', outline: 'none',
              }}
            >
              <option value="">Todas mis carteras</option>
              {carteras.map(c => (
                <option key={c.id} value={c.id}>{c.nombreCartera}</option>
              ))}
            </select>
          </div>

          {carteraID !== null && (
            <button
              onClick={() => { setCarteraID(null); setCarteraNombre(''); }}
              style={{
                padding: '7px 14px', borderRadius: '6px',
                border: '0.5px solid var(--z-border)', background: 'var(--z-bg-page)',
                color: 'var(--z-text-muted)', fontSize: '12px', cursor: 'pointer',
              }}
            >
              Ver todas
            </button>
          )}
        </div>

        {/* Período */}
        <div>
          <p style={{ fontSize: '11px', fontWeight: 600, color: 'var(--z-text-muted)', textTransform: 'uppercase', letterSpacing: '0.04em', marginBottom: '8px' }}>
            Período
          </p>
          <div style={{ display: 'flex', gap: '6px', flexWrap: 'wrap', alignItems: 'center' }}>
            {PRESETS.map(p => (
              <button key={p.valor} onClick={() => setPreset(p.valor)} style={estiloBtnPreset(preset === p.valor)}>
                {p.label}
              </button>
            ))}
          </div>

          {preset === 'ultimo' && data && (
            <p style={{ fontSize: '12px', color: 'var(--z-text-muted)', marginTop: '8px' }}>
              Datos del ciclo: <strong>{formatearFechaCorta(data.metricas.fechaDesde)}</strong>
            </p>
          )}

          {preset === 'custom' && (
            <div style={{ display: 'flex', gap: '10px', marginTop: '10px', flexWrap: 'wrap', alignItems: 'flex-end' }}>
              <div style={{ display: 'flex', flexDirection: 'column', gap: '4px' }}>
                <label style={{ fontSize: '11px', color: 'var(--z-text-muted)' }}>Desde</label>
                <input
                  type="date" value={customDesde} onChange={e => setCustomDesde(e.target.value)}
                  style={{ padding: '6px 10px', fontSize: '13px', borderRadius: '6px', border: '0.5px solid var(--z-border)', background: 'var(--z-bg-page)', color: 'var(--z-text-primary)', outline: 'none' }}
                />
              </div>
              <div style={{ display: 'flex', flexDirection: 'column', gap: '4px' }}>
                <label style={{ fontSize: '11px', color: 'var(--z-text-muted)' }}>Hasta</label>
                <input
                  type="date" value={customHasta} onChange={e => setCustomHasta(e.target.value)}
                  style={{ padding: '6px 10px', fontSize: '13px', borderRadius: '6px', border: '0.5px solid var(--z-border)', background: 'var(--z-bg-page)', color: 'var(--z-text-primary)', outline: 'none' }}
                />
              </div>
            </div>
          )}
        </div>
      </div>

      {/* Error */}
      {isError && (
        <div style={{
          background: '#fff5f5', border: '0.5px solid #fecaca',
          borderRadius: '8px', padding: '14px 16px',
          fontSize: '13px', color: '#dc2626',
        }}>
          No se pudo cargar el dashboard. Intenta de nuevo.
        </div>
      )}

      {/* Métricas */}
      <section>
        <div style={{ marginBottom: '12px', display: 'flex', alignItems: 'baseline', justifyContent: 'space-between', flexWrap: 'wrap', gap: '8px' }}>
          <div>
            <h2 style={{ fontSize: '13px', fontWeight: 600, color: 'var(--z-text-primary)', marginBottom: '2px' }}>
              Métricas del período
            </h2>
            {data && (
              <p style={{ fontSize: '12px', color: 'var(--z-text-muted)' }}>
                {formatearFechaCorta(data.metricas.fechaDesde)} — {formatearFechaCorta(data.metricas.fechaHasta)}
              </p>
            )}
          </div>
          {data && (
            <span style={{ fontSize: '12px', color: 'var(--z-text-muted)' }}>
              Actualizado: {formatearFechaHora(data.metricas.fechaActualizacion)}
            </span>
          )}
        </div>

        {isLoading ? (
          <CargandoSpiral mensaje="Cargando métricas..." />
        ) : data ? (
          <div style={{ display: 'flex', gap: '12px', flexWrap: 'wrap', alignItems: 'stretch' }}>
            <TarjetaAvance
              gestionados={data.metricas.totalGestiones}
              asignados={data.metricas.totalAsignados}
            />
            <TarjetaMetrica
              label="Contactabilidad"
              valor={pct(data.metricas.tasaContactabilidad)}
              sub="Contactos positivos / total gestiones"
            />
            <TarjetaMetrica
              label="Efectividad"
              valor={pct(data.metricas.tasaEfectividad)}
              sub="Acuerdos y abonos / contactos positivos"
            />
            <TarjetaMetrica
              label="Proyección promesas"
              valor={formatearPesos(data.metricas.proyeccionPromesas)}
              sub="Suma de acuerdos y abonos del período"
              valorSize={18}
            />
            <TarjetaMetrica
              label="Incumplidos"
              valor={data.metricas.totalIncumplidos.toLocaleString('es-CO')}
              sub="Titulares con tipificación Incumplido"
            />
          </div>
        ) : null}
      </section>

      {/* Desglose por asesor */}
      <section>
        <h2 style={{ fontSize: '13px', fontWeight: 600, color: 'var(--z-text-primary)', marginBottom: '12px' }}>
          Desglose por asesor
        </h2>
        <div style={{
          background: 'var(--z-bg-card)', border: '0.5px solid var(--z-border)',
          borderRadius: '8px', overflow: 'hidden',
        }}>
          {isLoading ? (
            <CargandoSpiral mensaje="Cargando asesores..." />
          ) : (
            <TablaAsesores filas={data?.filasAsesor ?? []} />
          )}
        </div>
      </section>

    </div>
  );
}
