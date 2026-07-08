import { useState } from 'react';
import { useQuery } from '@tanstack/react-query';
import {
  obtenerDashboardDirector,
  obtenerLideresDropdown,
  obtenerCarterasLider,
} from '../../api/dashboardApi';
import type { FilaLiderDto, FilaAsesorDto, MetricasDirectorDto } from '../../types/dashboard';
import { CargandoSpiral } from '../../components/CargandoSpiral';
import { ThOrdenable } from '../../components/ThOrdenable';
import { useSort } from '../../hooks/useSort';
import { formatearTiempoTranscurrido } from '../../utils/formatters';

// ── Tipos ─────────────────────────────────────────────────────────────────────

type Preset = 'hoy' | 'ayer' | 'ciclo' | 'mes-pasado' | '7d' | '30d' | 'custom';

interface Rango { desde: string; hasta: string; }

// ── Helpers ───────────────────────────────────────────────────────────────────

function isoHoy(): string {
  return new Date().toISOString().slice(0, 10);
}

function isoFecha(d: Date): string {
  return d.toISOString().slice(0, 10);
}

function calcularRango(preset: Preset, customDesde: string, customHasta: string): Rango | null {
  const hoy = new Date();
  switch (preset) {
    case 'hoy':
      return { desde: isoHoy(), hasta: isoHoy() };
    case 'ayer': {
      const ayer = new Date(hoy);
      ayer.setDate(hoy.getDate() - 1);
      return { desde: isoFecha(ayer), hasta: isoFecha(ayer) };
    }
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

// ── Constantes de presets ─────────────────────────────────────────────────────

const PRESETS: { valor: Preset; label: string }[] = [
  { valor: 'ciclo',      label: 'Este mes' },
  { valor: 'hoy',        label: 'Hoy' },
  { valor: 'ayer',       label: 'Ayer' },
  { valor: 'mes-pasado', label: 'Mes pasado' },
  { valor: '7d',         label: 'Últimos 7 días' },
  { valor: '30d',        label: 'Últimos 30 días' },
  { valor: 'custom',     label: 'Personalizado' },
];

// ── Sub-componentes ───────────────────────────────────────────────────────────

function TarjetaMetrica({ label, valor, sub, valorSize = 26, alerta }: {
  label: string; valor: string; sub?: string; valorSize?: number;
  alerta?: { cantidad: number; onClick: () => void };
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
      {alerta && (
        <button
          onClick={alerta.onClick}
          title="Hay gestiones con un valor de promesa irregular que no se sumaron a la proyección"
          style={{
            marginTop: '8px', display: 'inline-flex', alignItems: 'center', gap: '5px',
            fontSize: '11px', fontWeight: 600, color: '#854F0B', background: '#FAEEDA',
            border: 'none', borderRadius: '6px', padding: '4px 8px', cursor: 'pointer',
          }}
        >
          <svg width="12" height="12" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="2.5" strokeLinecap="round" strokeLinejoin="round">
            <path d="M12 9v4M12 17h.01M10.29 3.86 1.82 18a2 2 0 0 0 1.71 3h16.94a2 2 0 0 0 1.71-3L13.71 3.86a2 2 0 0 0-3.42 0Z" />
          </svg>
          {alerta.cantidad} gestión{alerta.cantidad === 1 ? '' : 'es'} irregular{alerta.cantidad === 1 ? '' : 'es'}
        </button>
      )}
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
        {/* Barra lineal de soporte visual */}
        <div style={{ marginTop: '10px', height: 4, borderRadius: 99, background: 'var(--z-border)', overflow: 'hidden' }}>
          <div style={{ height: '100%', width: `${pctNum}%`, background: color, borderRadius: 99, transition: 'width 0.6s ease' }} />
        </div>
      </div>
    </div>
  );
}

function TablaLideres({ filas, onSeleccionar }: {
  filas: FilaLiderDto[];
  onSeleccionar: (liderID: number, nombre: string) => void;
}) {
  const { items, sort, toggle } = useSort<FilaLiderDto>(
    filas,
    { key: 'totalGestiones', dir: 'desc' },
    (f, k) => (f as unknown as Record<string, string | number>)[k],
  );

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
          <ThOrdenable label="Líder"           sortKey="nombreLider"         sort={sort} onSort={toggle} />
          <ThOrdenable label="Gestiones"       sortKey="totalGestiones"      sort={sort} onSort={toggle} align="right" />
          <ThOrdenable label="Contactabilidad" sortKey="tasaContactabilidad" sort={sort} onSort={toggle} align="right" />
          <ThOrdenable label="Efectividad"     sortKey="tasaEfectividad"     sort={sort} onSort={toggle} align="right" />
          <ThOrdenable label="Proyección"      sortKey="proyeccionPromesas"  sort={sort} onSort={toggle} align="right" />
          <ThOrdenable label="" align="center" />
        </tr>
      </thead>
      <tbody>
        {items.map(f => (
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
            <td style={{ padding: '11px 14px', fontSize: '13px', color: 'var(--z-text-primary)', textAlign: 'right', fontWeight: 500 }}>
              {formatearPesos(f.proyeccionPromesas)}
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

function ModalGestionesExcluidas({ metricas, onClose }: { metricas: MetricasDirectorDto; onClose: () => void }) {
  return (
    <div style={{
      position: 'fixed', inset: 0,
      background: 'rgba(0,0,0,0.4)',
      display: 'flex', alignItems: 'center', justifyContent: 'center',
      zIndex: 100,
    }}>
      <div style={{
        background: 'var(--z-bg-card)',
        border: '0.5px solid var(--z-border)',
        borderRadius: '12px',
        padding: '28px 32px',
        maxWidth: '440px',
        width: '90%',
        boxShadow: '0 8px 32px rgba(0,0,0,0.12)',
      }}>
        <p style={{ fontSize: '15px', fontWeight: 700, color: 'var(--z-text-primary)', marginBottom: '6px' }}>
          Gestiones excluidas de la proyección
        </p>
        <p style={{ fontSize: '13px', color: 'var(--z-text-muted)', marginBottom: '20px', lineHeight: 1.5 }}>
          No se incluyeron en la "Proyección promesas" por tener un valor de promesa que parece irregular.
        </p>

        <div style={{ display: 'flex', gap: '24px', marginBottom: '20px' }}>
          <div>
            <p style={{ fontSize: '10px', fontWeight: 600, color: 'var(--z-text-muted)', textTransform: 'uppercase', letterSpacing: '0.04em', marginBottom: '4px' }}>
              Gestiones
            </p>
            <p style={{ fontSize: '18px', fontWeight: 700, color: '#854F0B' }}>
              {metricas.gestionesExcluidasPorTecho}
            </p>
          </div>
          <div>
            <p style={{ fontSize: '10px', fontWeight: 600, color: 'var(--z-text-muted)', textTransform: 'uppercase', letterSpacing: '0.04em', marginBottom: '4px' }}>
              Valor excluido
            </p>
            <p style={{ fontSize: '18px', fontWeight: 700, color: '#854F0B' }}>
              {formatearPesos(metricas.valorExcluidoPorTecho)}
            </p>
          </div>
        </div>

        <p style={{ fontSize: '10px', fontWeight: 600, color: 'var(--z-text-muted)', textTransform: 'uppercase', letterSpacing: '0.04em', marginBottom: '8px' }}>
          Titulares a revisar
        </p>
        <div style={{ display: 'flex', gap: '8px', flexWrap: 'wrap', marginBottom: '24px' }}>
          {metricas.titularesExcluidos.map(id => (
            <span key={id} style={{
              fontFamily: 'ui-monospace, monospace', fontSize: '12px', color: 'var(--z-text-primary)',
              background: 'var(--z-bg-page)', border: '0.5px solid var(--z-border)',
              borderRadius: '6px', padding: '4px 10px',
            }}>
              {id}
            </span>
          ))}
        </div>

        <div style={{ display: 'flex', justifyContent: 'flex-end' }}>
          <button
            onClick={onClose}
            style={{
              padding: '8px 16px',
              borderRadius: '6px',
              border: '1px solid var(--z-border)',
              background: 'transparent',
              color: 'var(--z-text-muted)',
              fontSize: '13px',
              cursor: 'pointer',
            }}
          >
            Cerrar
          </button>
        </div>
      </div>
    </div>
  );
}

// ── Página principal ──────────────────────────────────────────────────────────

export function DashboardDirectorPage() {
  const [liderID,       setLiderID]       = useState<number | null>(null);
  const [liderNombre,   setLiderNombre]   = useState<string>('');
  const [carteraID,     setCarteraID]     = useState<number | null>(null);
  const [carteraNombre, setCarteraNombre] = useState<string>('');
  const [modalExcluidosAbierto, setModalExcluidosAbierto] = useState(false);

  // Selector de fechas
  const [preset,      setPreset]      = useState<Preset>('ciclo');
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
      <div style={{ display: 'flex', justifyContent: 'space-between', alignItems: 'flex-start', flexWrap: 'wrap', gap: 12 }}>
        <div>
          <h1 style={{ fontSize: 20, fontWeight: 600, color: 'var(--z-text-primary)', margin: 0 }}>
            Dashboard Director
          </h1>
          <p style={{ fontSize: 13, color: 'var(--z-text-muted)', margin: '4px 0 0' }}>
            Rendimiento operativo &mdash; <span style={{ fontWeight: 500, color: 'var(--z-text-primary)' }}>{tituloFiltro}</span>
          </p>
        </div>
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
              Actualizado: {formatearTiempoTranscurrido(data.metricas.fechaActualizacion)}
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
              alerta={data.metricas.gestionesExcluidasPorTecho > 0
                ? { cantidad: data.metricas.gestionesExcluidasPorTecho, onClick: () => setModalExcluidosAbierto(true) }
                : undefined}
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

      {modalExcluidosAbierto && data && (
        <ModalGestionesExcluidas
          metricas={data.metricas}
          onClose={() => setModalExcluidosAbierto(false)}
        />
      )}

    </div>
  );
}
