import { useState } from 'react';
import { useQuery } from '@tanstack/react-query';
import {
  obtenerDashboardDirector,
  obtenerLideresDropdown,
  obtenerCarterasLider,
} from '../../api/dashboardApi';
import type { FilaLiderDto, FilaAsesorDto } from '../../types/dashboard';
import { CargandoSpiral } from '../../components/CargandoSpiral';

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

// Cuando preset es 'ultimo', desde y hasta son undefined → backend resuelve el último ciclo
function calcularRango(preset: Preset, customDesde: string, customHasta: string): Rango | null {
  if (preset === 'ultimo') return null; // sin fechas → backend usa MAX(FechaActualizacion)
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

// ── Constantes de presets ─────────────────────────────────────────────────────

const PRESETS: { valor: Preset; label: string }[] = [
  { valor: 'ultimo',     label: 'Último ciclo' },
  { valor: 'hoy',        label: 'Hoy' },
  { valor: 'ciclo',      label: 'Este mes' },
  { valor: 'mes-pasado', label: 'Mes pasado' },
  { valor: '7d',         label: 'Últimos 7 días' },
  { valor: '30d',        label: 'Últimos 30 días' },
  { valor: 'custom',     label: 'Personalizado' },
];

// ── Sub-componentes ───────────────────────────────────────────────────────────

function TarjetaMetrica({ label, valor, sub }: { label: string; valor: string; sub?: string }) {
  return (
    <div style={{
      background: 'var(--z-bg-card)',
      border: '0.5px solid var(--z-border)',
      borderRadius: '8px',
      padding: '20px',
      flex: 1,
      minWidth: 0,
    }}>
      <p style={{ fontSize: '11px', fontWeight: 600, color: 'var(--z-text-muted)', textTransform: 'uppercase', letterSpacing: '0.04em', marginBottom: '8px' }}>
        {label}
      </p>
      <p style={{ fontSize: '28px', fontWeight: 600, color: 'var(--z-text-primary)', lineHeight: 1 }}>
        {valor}
      </p>
      {sub && (
        <p style={{ fontSize: '11px', color: 'var(--z-text-muted)', marginTop: '6px' }}>{sub}</p>
      )}
    </div>
  );
}

function TarjetaAvance({ gestionados, asignados }: { gestionados: number; asignados: number }) {
  const pctNum   = asignados > 0 ? Math.min((gestionados / asignados) * 100, 100) : 0;
  const radio    = 40;
  const grosor   = 7;
  const circunf  = 2 * Math.PI * radio;
  const offset   = circunf * (1 - pctNum / 100);

  // Color según avance
  const color = pctNum >= 80 ? '#22c55e' : pctNum >= 50 ? '#f59e0b' : '#3b82f6';

  return (
    <div style={{
      background: 'var(--z-bg-card)',
      border: '0.5px solid var(--z-border)',
      borderRadius: '8px',
      padding: '20px',
      flex: 1,
      minWidth: 0,
      display: 'flex',
      alignItems: 'center',
      gap: '20px',
    }}>
      {/* Ring SVG */}
      <svg width="100" height="100" style={{ flexShrink: 0 }}>
        {/* Pista de fondo */}
        <circle
          cx="50" cy="50" r={radio}
          fill="none"
          stroke="var(--z-border)"
          strokeWidth={grosor}
        />
        {/* Arco de progreso */}
        <circle
          cx="50" cy="50" r={radio}
          fill="none"
          stroke={color}
          strokeWidth={grosor}
          strokeLinecap="round"
          strokeDasharray={circunf}
          strokeDashoffset={offset}
          transform="rotate(-90 50 50)"
          style={{ transition: 'stroke-dashoffset 0.6s ease' }}
        />
        {/* Texto central */}
        <text x="50" y="46" textAnchor="middle" dominantBaseline="middle"
          style={{ fontSize: '16px', fontWeight: 700, fill: 'var(--z-text-primary)', fontFamily: 'inherit' }}>
          {pctNum.toFixed(1)}%
        </text>
        <text x="50" y="62" textAnchor="middle" dominantBaseline="middle"
          style={{ fontSize: '9px', fill: 'var(--z-text-muted)', fontFamily: 'inherit' }}>
          avance
        </text>
      </svg>

      {/* Leyenda */}
      <div>
        <p style={{ fontSize: '11px', fontWeight: 600, color: 'var(--z-text-muted)', textTransform: 'uppercase', letterSpacing: '0.04em', marginBottom: '10px' }}>
          Avance de gestión
        </p>
        <p style={{ fontSize: '13px', color: 'var(--z-text-primary)', marginBottom: '4px' }}>
          <span style={{ fontWeight: 600 }}>{gestionados.toLocaleString('es-CO')}</span>
          <span style={{ color: 'var(--z-text-muted)' }}> gestionados</span>
        </p>
        <p style={{ fontSize: '13px', color: 'var(--z-text-muted)' }}>
          de {asignados.toLocaleString('es-CO')} asignados
        </p>
      </div>
    </div>
  );
}

function TablaLideres({ filas, onSeleccionar }: {
  filas: FilaLiderDto[];
  onSeleccionar: (liderID: number, nombre: string) => void;
}) {
  if (filas.length === 0) {
    return (
      <div style={{ padding: '32px', textAlign: 'center', fontSize: '13px', color: 'var(--z-text-muted)' }}>
        Sin datos de líderes para este período.
      </div>
    );
  }

  return (
    <table style={{ width: '100%', borderCollapse: 'collapse' }}>
      <thead>
        <tr style={{ background: 'var(--z-bg-page)' }}>
          {['Líder', 'Gestiones', 'Contactabilidad', 'Efectividad', ''].map((col, i) => (
            <th key={i} style={{
              padding: '10px 14px',
              textAlign: i === 0 ? 'left' : i === 4 ? 'center' : 'right',
              fontSize: '11px', fontWeight: 600, color: 'var(--z-text-muted)',
              borderBottom: '0.5px solid var(--z-border)',
              letterSpacing: '0.04em', textTransform: 'uppercase',
            }}>
              {col}
            </th>
          ))}
        </tr>
      </thead>
      <tbody>
        {filas.map(f => (
          <tr key={f.liderID} style={{ borderBottom: '0.5px solid var(--z-border)' }}>
            <td style={{ padding: '11px 14px', fontSize: '13px', color: 'var(--z-text-primary)', fontWeight: 500 }}>
              {f.nombreLider}
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
            <td style={{ padding: '11px 14px', textAlign: 'center' }}>
              <button
                onClick={() => onSeleccionar(f.liderID, f.nombreLider)}
                style={{
                  padding: '4px 12px', borderRadius: '6px', border: '0.5px solid var(--z-border)',
                  background: 'var(--z-bg-page)', color: 'var(--z-text-primary)',
                  fontSize: '11px', fontWeight: 600, cursor: 'pointer',
                }}
              >
                Ver detalle
              </button>
            </td>
          </tr>
        ))}
      </tbody>
    </table>
  );
}

function TablaAsesores({ filas }: { filas: FilaAsesorDto[] }) {
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
          {['Asesor', 'Gestiones', 'Contactabilidad', 'Efectividad'].map((col, i) => (
            <th key={i} style={{
              padding: '10px 14px',
              textAlign: i === 0 ? 'left' : 'right',
              fontSize: '11px', fontWeight: 600, color: 'var(--z-text-muted)',
              borderBottom: '0.5px solid var(--z-border)',
              letterSpacing: '0.04em', textTransform: 'uppercase',
            }}>
              {col}
            </th>
          ))}
        </tr>
      </thead>
      <tbody>
        {filas.map(f => (
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
          </tr>
        ))}
      </tbody>
    </table>
  );
}

// ── Página principal ──────────────────────────────────────────────────────────

export function DashboardDirectorPage() {
  const [liderID,       setLiderID]       = useState<number | null>(null);
  const [liderNombre,   setLiderNombre]   = useState<string>('');
  const [carteraID,     setCarteraID]     = useState<number | null>(null);
  const [carteraNombre, setCarteraNombre] = useState<string>('');

  // Selector de fechas
  const [preset,      setPreset]      = useState<Preset>('ultimo');
  const [customDesde, setCustomDesde] = useState<string>('');
  const [customHasta, setCustomHasta] = useState<string>('');

  const rango = calcularRango(preset, customDesde, customHasta);

  // Dropdowns
  const { data: lideres = [] } = useQuery({
    queryKey: ['director-lideres'],
    queryFn: obtenerLideresDropdown,
    staleTime: 10 * 60 * 1000,
  });

  const { data: carteras = [] } = useQuery({
    queryKey: ['director-carteras', liderID],
    queryFn: () => obtenerCarterasLider(liderID!),
    enabled: liderID !== null,
    staleTime: 5 * 60 * 1000,
  });

  // Dashboard — rango null → backend usa último ciclo
  const { data, isLoading, isError } = useQuery({
    queryKey: ['dashboard-director', liderID, carteraID, rango?.desde ?? null, rango?.hasta ?? null],
    queryFn: () => obtenerDashboardDirector(liderID, carteraID, rango?.desde, rango?.hasta),
    staleTime: 3 * 60 * 1000,
  });

  const tituloFiltro = carteraNombre
    ? carteraNombre
    : liderNombre
      ? liderNombre
      : 'Todos los líderes';

  const handleSeleccionarLider = (id: number, nombre: string) => {
    setLiderID(id);
    setLiderNombre(nombre);
    setCarteraID(null);
    setCarteraNombre('');
  };

  const handleCambiarLider = (e: React.ChangeEvent<HTMLSelectElement>) => {
    const val = Number(e.target.value);
    if (!val) {
      setLiderID(null);
      setLiderNombre('');
      setCarteraID(null);
      setCarteraNombre('');
    } else {
      const lider = lideres.find(l => l.id === val);
      setLiderID(val);
      setLiderNombre(lider?.nombre ?? '');
      setCarteraID(null);
      setCarteraNombre('');
    }
  };

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

  const limpiarFiltros = () => {
    setLiderID(null);
    setLiderNombre('');
    setCarteraID(null);
    setCarteraNombre('');
  };

  const mostrarDesgloseLider  = !liderID && (data?.filasLider?.length ?? 0) > 0;
  const mostrarDesgloseAsesor = liderID !== null;

  // Estilo de botón preset
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

  return (
    <div style={{ display: 'flex', flexDirection: 'column', gap: '24px', maxWidth: '1000px' }}>

      {/* Encabezado */}
      <div>
        <h1 style={{ fontSize: '16px', fontWeight: 600, color: 'var(--z-text-primary)', marginBottom: '4px' }}>
          Dashboard Director
        </h1>
        <p style={{ fontSize: '13px', color: 'var(--z-text-muted)' }}>
          Rendimiento operativo &mdash; <span style={{ fontWeight: 500, color: 'var(--z-text-primary)' }}>{tituloFiltro}</span>
        </p>
      </div>

      {/* Filtros */}
      <div style={{
        background: 'var(--z-bg-card)', border: '0.5px solid var(--z-border)',
        borderRadius: '8px', padding: '16px 20px',
        display: 'flex', flexDirection: 'column', gap: '14px',
      }}>
        {/* Fila 1: Líder + Cartera */}
        <div style={{ display: 'flex', gap: '12px', alignItems: 'flex-end', flexWrap: 'wrap' }}>
          <div style={{ display: 'flex', flexDirection: 'column', gap: '4px', minWidth: '200px' }}>
            <label style={{ fontSize: '11px', fontWeight: 600, color: 'var(--z-text-muted)', textTransform: 'uppercase', letterSpacing: '0.04em' }}>
              Líder
            </label>
            <select
              value={liderID ?? ''}
              onChange={handleCambiarLider}
              style={{
                padding: '7px 10px', fontSize: '13px', borderRadius: '6px',
                border: '0.5px solid var(--z-border)', background: 'var(--z-bg-page)',
                color: 'var(--z-text-primary)', outline: 'none',
              }}
            >
              <option value="">Todos los líderes</option>
              {lideres.map(l => (
                <option key={l.id} value={l.id}>{l.nombre}</option>
              ))}
            </select>
          </div>

          {liderID !== null && (
            <div style={{ display: 'flex', flexDirection: 'column', gap: '4px', minWidth: '200px' }}>
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
                <option value="">Todas las carteras del líder</option>
                {carteras.map(c => (
                  <option key={c.id} value={c.id}>{c.nombreCartera}</option>
                ))}
              </select>
            </div>
          )}

          {(liderID !== null || carteraID !== null) && (
            <button
              onClick={limpiarFiltros}
              style={{
                padding: '7px 14px', borderRadius: '6px',
                border: '0.5px solid var(--z-border)', background: 'var(--z-bg-page)',
                color: 'var(--z-text-muted)', fontSize: '12px', cursor: 'pointer',
              }}
            >
              Limpiar filtros
            </button>
          )}
        </div>

        {/* Fila 2: Selector de período */}
        <div>
          <p style={{ fontSize: '11px', fontWeight: 600, color: 'var(--z-text-muted)', textTransform: 'uppercase', letterSpacing: '0.04em', marginBottom: '8px' }}>
            Período
          </p>
          <div style={{ display: 'flex', gap: '6px', flexWrap: 'wrap', alignItems: 'center' }}>
            {PRESETS.map(p => (
              <button
                key={p.valor}
                onClick={() => setPreset(p.valor)}
                style={estiloBtnPreset(preset === p.valor)}
              >
                {p.label}
              </button>
            ))}
          </div>

          {/* Date pickers para rango personalizado */}
          {/* Indicador visual del rango activo cuando lo resuelve el backend */}
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
                  type="date"
                  value={customDesde}
                  onChange={e => setCustomDesde(e.target.value)}
                  style={{
                    padding: '6px 10px', fontSize: '13px', borderRadius: '6px',
                    border: '0.5px solid var(--z-border)', background: 'var(--z-bg-page)',
                    color: 'var(--z-text-primary)', outline: 'none',
                  }}
                />
              </div>
              <div style={{ display: 'flex', flexDirection: 'column', gap: '4px' }}>
                <label style={{ fontSize: '11px', color: 'var(--z-text-muted)' }}>Hasta</label>
                <input
                  type="date"
                  value={customHasta}
                  onChange={e => setCustomHasta(e.target.value)}
                  style={{
                    padding: '6px 10px', fontSize: '13px', borderRadius: '6px',
                    border: '0.5px solid var(--z-border)', background: 'var(--z-bg-page)',
                    color: 'var(--z-text-primary)', outline: 'none',
                  }}
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
          <div style={{ display: 'flex', gap: '12px', flexWrap: 'wrap' }}>
            <TarjetaAvance
              gestionados={data.metricas.totalGestiones}
              asignados={data.metricas.totalAsignados}
            />
            <TarjetaMetrica
              label="Contactabilidad"
              valor={pct(data.metricas.tasaContactabilidad)}
              sub="Contactos positivos / Total gestiones"
            />
            <TarjetaMetrica
              label="Efectividad"
              valor={pct(data.metricas.tasaEfectividad)}
              sub="Acuerdos y abonos / Contactos positivos"
            />
            <TarjetaMetrica
              label="Proyección promesas"
              valor={formatearPesos(data.metricas.proyeccionPromesas)}
              sub="Suma de acuerdos y abonos del período"
            />
            <TarjetaMetrica
              label="Incumplidos"
              valor={data.metricas.totalIncumplidos.toLocaleString('es-CO')}
              sub="Titulares con tipificación Incumplido"
            />
          </div>
        ) : null}
      </section>

      {/* Desglose por líder (vista global) */}
      {mostrarDesgloseLider && (
        <section>
          <h2 style={{ fontSize: '13px', fontWeight: 600, color: 'var(--z-text-primary)', marginBottom: '12px' }}>
            Desglose por líder
          </h2>
          <div style={{
            background: 'var(--z-bg-card)', border: '0.5px solid var(--z-border)',
            borderRadius: '8px', overflow: 'hidden',
          }}>
            <TablaLideres
              filas={data!.filasLider}
              onSeleccionar={handleSeleccionarLider}
            />
          </div>
        </section>
      )}

      {/* Desglose por asesor (vista filtrada por líder o cartera) */}
      {mostrarDesgloseAsesor && (
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
      )}

    </div>
  );
}
