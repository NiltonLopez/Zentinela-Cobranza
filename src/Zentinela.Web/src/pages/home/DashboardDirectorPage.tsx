import { useState, useMemo } from 'react';
import { useQuery } from '@tanstack/react-query';
import { useAuth } from '../../context/AuthContext';
import {
  obtenerDashboardDirector,
  obtenerLideresDropdown,
  obtenerCarterasLider,
} from '../../api/dashboardApi';
import { getCategoriasAgentes, getInformeLideres } from '../../api/informesRecaudoApi';
import type { FilaLiderDto, FilaAsesorDto, MetricasDirectorDto } from '../../types/dashboard';
import type { CategoriaAgenteDto } from '../../types/informesRecaudo';
import { CargandoSpiral } from '../../components/CargandoSpiral';
import { ZDropdown } from '../../components/ZDropdown';
import { ThOrdenable } from '../../components/ThOrdenable';
import { IconoCategoria, type CategoriaRanking } from '../../components/IconoCategoria';
import { useSort } from '../../hooks/useSort';
import { useDuracionTicker } from '../../hooks/useDuracionTicker';
import { formatearTiempoTranscurrido } from '../../utils/formatters';
import { MARGEN_BARRA_COMPLETA } from '../../utils/layout';
import { calcularLiderDelMes, type LiderDelMes } from '../../utils/liderDelMes';

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

/** Como formatearPesos, pero separa el número de la unidad "millones" cuando el valor es grande, para poder mostrar la unidad en un tamaño más chico sin que se corte de línea. */
function formatearPesosCorto(valor: number): { valor: string; unidad?: string } {
  if (Math.abs(valor) < 1_000_000) return { valor: formatearPesos(valor) };
  // Truncamos (no redondeamos) los millones para nunca mostrar un valor mayor al real.
  // Ej: 1.181,94 millones → "$ 1.181 millones", nunca "$ 1.182 millones". El valor exacto se muestra aparte (valorReal).
  const millones = Math.trunc(valor / 1_000_000);
  return { valor: `$ ${millones.toLocaleString('es-CO')}`, unidad: 'millones' };
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

function TarjetaMetrica({ label, valor, unidad, valorReal, sub, valorSize = 26, alerta }: {
  label: string; valor: string; unidad?: string; valorReal?: string; sub?: string; valorSize?: number;
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
      display: 'flex',
      flexDirection: 'column',
      boxShadow: '0 1px 3px rgba(0,0,0,0.06), 0 1px 2px rgba(0,0,0,0.04)',
    }}>
      <p style={{ fontSize: '11px', fontWeight: 600, color: 'var(--z-text-primary)', textTransform: 'uppercase', letterSpacing: '0.04em', marginBottom: '10px' }}>
        {label}
      </p>
      <div style={{ flex: 1, display: 'flex', flexDirection: 'column', justifyContent: 'center' }}>
        <p style={{ fontSize: `${valorSize}px`, fontWeight: 700, color: 'var(--z-brand)', lineHeight: 1, whiteSpace: 'nowrap' }}>
          {valor}
          {unidad && (
            <span style={{ fontSize: '13px', fontWeight: 600, marginLeft: '4px' }}>{unidad}</span>
          )}
        </p>
        {valorReal && (
          <p style={{ fontSize: '11px', color: 'var(--z-text-muted)', marginTop: '4px', lineHeight: 1.4 }}>{valorReal}</p>
        )}
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
    </div>
  );
}

const LABEL_LIDER_DEL_MES: Record<LiderDelMes['tipo'], string> = {
  'cumplimiento':       'Líder actual del mes',
  'honorarios-totales': 'Más honorarios recaudados',
  'productividad':      'Mejor productividad',
};

function BarraDestacadosLider({ filas, liderDelMes }: { filas: FilaLiderDto[]; liderDelMes: LiderDelMes[] }) {
  // Líder destacado por categoría — si el mismo líder gana varias, su nombre se repite.
  const items = useMemo(() => {
    if (filas.length === 0) return [];
    const masGestiones       = filas.reduce((a, b) => (b.totalGestiones      > a.totalGestiones      ? b : a));
    const masContactabilidad = filas.reduce((a, b) => (b.tasaContactabilidad > a.tasaContactabilidad ? b : a));
    const masEfectividad     = filas.reduce((a, b) => (b.tasaEfectividad     > a.tasaEfectividad      ? b : a));
    const masProyeccion      = filas.reduce((a, b) => (b.proyeccionPromesas  > a.proyeccionPromesas    ? b : a));
    const destacados = [
      { label: 'Más gestiones',        nombre: masGestiones.nombreLider,       valor: masGestiones.totalGestiones.toLocaleString('es-CO') },
      { label: 'Mayor contactabilidad', nombre: masContactabilidad.nombreLider, valor: pct(masContactabilidad.tasaContactabilidad) },
      { label: 'Mayor efectividad',    nombre: masEfectividad.nombreLider,     valor: pct(masEfectividad.tasaEfectividad) },
      { label: 'Mayor proyección',     nombre: masProyeccion.nombreLider,      valor: formatearPesos(masProyeccion.proyeccionPromesas) },
    ];
    // Plan B: sin metas cargadas ese mes (o todas en 0%) — honorarios totales y productividad se
    // destacan por separado, cada uno con su propio ganador.
    liderDelMes.forEach(item => {
      destacados.push({
        label: LABEL_LIDER_DEL_MES[item.tipo],
        nombre: item.nombreLider,
        valor: item.tipo === 'cumplimiento' ? pct(item.valor) : formatearPesos(item.valor),
      });
    });
    return destacados;
  }, [filas, liderDelMes]);

  const [pausado, setPausado] = useState(false);
  const { ref: pistaRef, duracion: duracionAnimacion } = useDuracionTicker([items]);

  if (items.length === 0) return null;

  // Pista duplicada: al recorrer el 50% del ancho total vuelve a verse igual → loop continuo sin salto.
  const pista = [...items, ...items];

  return (
    <div
      onMouseEnter={() => setPausado(true)}
      onMouseLeave={() => setPausado(false)}
      style={{
        background: 'var(--z-brand)',
        borderRadius: '2px', padding: '8px 0',
        boxShadow: '0 1px 3px rgba(0,0,0,0.06), 0 1px 2px rgba(0,0,0,0.04)',
        overflow: 'hidden',
        marginLeft: MARGEN_BARRA_COMPLETA, marginRight: MARGEN_BARRA_COMPLETA, marginTop: '-22px', marginBottom: '24px',
      }}
    >
      <style>{`
        @keyframes zentinelaDestacadosLectura {
          from { transform: translateX(0%); }
          to   { transform: translateX(-50%); }
        }
      `}</style>
      <div ref={pistaRef} style={{
        display: 'flex', width: 'max-content',
        animationName: 'zentinelaDestacadosLectura',
        animationDuration: duracionAnimacion,
        animationTimingFunction: 'linear',
        animationIterationCount: 'infinite',
        animationPlayState: pausado ? 'paused' : 'running',
      }}>
        {pista.map((it, i) => (
          <div key={`${it.label}-${i}`} style={{
            display: 'flex', alignItems: 'baseline', gap: '7px',
            padding: '0 24px', borderRight: '1px solid rgba(255,255,255,0.3)',
            whiteSpace: 'nowrap',
          }}>
            <span style={{ fontSize: '12px', fontWeight: 700, color: 'rgba(255,255,255,0.8)', textTransform: 'uppercase', letterSpacing: '0.04em' }}>
              {it.label}
            </span>
            <span style={{ fontSize: '15px', color: '#fff', fontWeight: 600, textTransform: 'uppercase' }}>
              {it.nombre}
              {' · '}{it.valor}
            </span>
          </div>
        ))}
      </div>
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
      boxShadow: '0 1px 3px rgba(0,0,0,0.06), 0 1px 2px rgba(0,0,0,0.04)',
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
        <p style={{ fontSize: '11px', fontWeight: 600, color: 'var(--z-text-primary)', textTransform: 'uppercase', letterSpacing: '0.04em', marginBottom: '10px' }}>
          Avance de gestión
        </p>
        <p style={{ fontSize: '13px', marginBottom: '4px' }}>
          <span style={{ fontWeight: 700, color: 'var(--z-brand)' }}>{gestionados.toLocaleString('es-CO')}</span>
          <span style={{ color: 'var(--z-text-muted)' }}> gestionados</span>
        </p>
        <p style={{ fontSize: '13px', color: 'var(--z-text-muted)' }}>
          de {asignados.toLocaleString('es-CO')} asignados
        </p>
        {/* Barra lineal de soporte visual */}
        <div style={{ marginTop: '10px', height: 4, borderRadius: 99, background: 'var(--z-border)', overflow: 'hidden' }}>
          <div style={{ height: '100%', width: `${pctNum}%`, background: 'var(--z-brand)', borderRadius: 99, transition: 'width 0.6s ease' }} />
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
  const [filaHover, setFilaHover] = useState<number | null>(null);

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
        <tr style={{ background: 'var(--z-brand-light)' }}>
          <ThOrdenable label="Líder"           sortKey="nombreLider"         sort={sort} onSort={toggle} />
          <ThOrdenable label="Gestiones"       sortKey="totalGestiones"      sort={sort} onSort={toggle} align="right" />
          <ThOrdenable label="Contactabilidad" sortKey="tasaContactabilidad" sort={sort} onSort={toggle} align="right" />
          <ThOrdenable label="Efectividad"     sortKey="tasaEfectividad"     sort={sort} onSort={toggle} align="right" />
          <ThOrdenable label="Proyección"      sortKey="proyeccionPromesas"  sort={sort} onSort={toggle} align="right" />
          <ThOrdenable label="" align="center" />
        </tr>
      </thead>
      <tbody>
        {items.map((f, i) => (
          <tr
            key={f.liderID}
            onMouseEnter={() => setFilaHover(f.liderID)}
            onMouseLeave={() => setFilaHover(prev => (prev === f.liderID ? null : prev))}
            style={{
              borderTop: i === 0 ? '2px solid var(--z-brand)' : undefined,
              borderBottom: '0.5px solid var(--z-border)',
              background: filaHover === f.liderID ? 'var(--z-bg-page)' : 'transparent',
              transition: 'background 0.12s ease',
            }}
          >
            <td style={{ padding: '12px 14px', fontSize: '13px', color: 'var(--z-text-primary)', fontWeight: 500 }}>
              {f.nombreLider}
            </td>
            <td style={{ padding: '12px 14px', fontSize: '13px', color: 'var(--z-text-primary)', textAlign: 'right', fontVariantNumeric: 'tabular-nums' }}>
              {f.totalGestiones.toLocaleString('es-CO')}
            </td>
            <td style={{ padding: '12px 14px', fontSize: '13px', color: 'var(--z-text-primary)', textAlign: 'right', fontVariantNumeric: 'tabular-nums' }}>
              {pct(f.tasaContactabilidad)}
            </td>
            <td style={{ padding: '12px 14px', fontSize: '13px', color: 'var(--z-text-primary)', textAlign: 'right', fontVariantNumeric: 'tabular-nums' }}>
              {pct(f.tasaEfectividad)}
            </td>
            <td style={{ padding: '12px 14px', fontSize: '13px', color: 'var(--z-text-primary)', textAlign: 'right', fontWeight: 500, fontVariantNumeric: 'tabular-nums' }}>
              {formatearPesos(f.proyeccionPromesas)}
            </td>
            <td style={{ padding: '12px 14px', textAlign: 'center' }}>
              <button
                onClick={() => onSeleccionar(f.liderID, f.nombreLider)}
                style={{
                  padding: '4px 12px', borderRadius: '6px', border: '0.5px solid var(--z-border)',
                  background: filaHover === f.liderID ? 'var(--z-bg-card)' : 'var(--z-bg-page)',
                  color: 'var(--z-text-primary)',
                  fontSize: '11px', fontWeight: 600, cursor: 'pointer',
                  boxShadow: filaHover === f.liderID ? '0 1px 3px rgba(0,0,0,0.08)' : 'none',
                  transition: 'background 0.12s ease, box-shadow 0.12s ease',
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

function TablaAsesores({ filas, categoriaPorAgente }: {
  filas: FilaAsesorDto[]; categoriaPorAgente: Map<number, CategoriaAgenteDto>;
}) {
  const { items, sort, toggle } = useSort<FilaAsesorDto>(
    filas,
    { key: 'totalGestiones', dir: 'desc' },
    (f, k) => k === 'categoria'
      ? categoriaPorAgente.get(f.agenteID)?.promedioAnual ?? null
      : (f as unknown as Record<string, string | number>)[k],
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
        <tr style={{ background: 'var(--z-brand-light)' }}>
          <ThOrdenable label="Asesor"          sortKey="nombreAsesor"        sort={sort} onSort={toggle} />
          <ThOrdenable label="Gestiones"       sortKey="totalGestiones"      sort={sort} onSort={toggle} align="right" />
          <ThOrdenable label="Contactabilidad" sortKey="tasaContactabilidad" sort={sort} onSort={toggle} align="right" />
          <ThOrdenable label="Efectividad"     sortKey="tasaEfectividad"     sort={sort} onSort={toggle} align="right" />
          <ThOrdenable label="Proyección"      sortKey="proyeccionPromesas"  sort={sort} onSort={toggle} align="right" />
          {categoriaPorAgente.size > 0 && (
            <ThOrdenable label="Categoría" sortKey="categoria" sort={sort} onSort={toggle} align="center" />
          )}
        </tr>
      </thead>
      <tbody>
        {items.map((f, i) => {
          const categoria = categoriaPorAgente.get(f.agenteID);
          return (
          <tr
            key={f.agenteID}
            style={{
              borderTop: i === 0 ? '2px solid var(--z-brand)' : undefined,
              borderBottom: '0.5px solid var(--z-border)',
            }}
            onMouseEnter={(e) => { e.currentTarget.style.background = 'var(--z-bg-page)'; }}
            onMouseLeave={(e) => { e.currentTarget.style.background = 'transparent'; }}
          >
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
            {categoriaPorAgente.size > 0 && (
              <td style={{ padding: '11px 14px', fontSize: '13px', textAlign: 'center', whiteSpace: 'nowrap' }}>
                {categoria
                  ? (
                    <span
                      title={`${categoria.categoria}${categoria.ajustePorBajoRendimiento ? ' (bajó por rendimiento vs. mes anterior)' : ''}`}
                      style={{ display: 'inline-flex', alignItems: 'center', gap: 5 }}
                    >
                      <IconoCategoria categoria={categoria.categoria as CategoriaRanking} size={22} />
                      <span style={{ color: 'var(--z-text-muted)' }}>{formatearPesos(categoria.promedioAnual)}</span>
                    </span>
                  )
                  : <span style={{ color: 'var(--z-text-muted)', fontSize: 12 }}>—</span>}
              </td>
            )}
          </tr>
          );
        })}
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
            <p style={{ fontSize: '18px', fontWeight: 700, color: '#C2410C' }}>
              {metricas.gestionesExcluidasPorTecho}
            </p>
          </div>
          <div>
            <p style={{ fontSize: '10px', fontWeight: 600, color: 'var(--z-text-muted)', textTransform: 'uppercase', letterSpacing: '0.04em', marginBottom: '4px' }}>
              Valor excluido
            </p>
            <p style={{ fontSize: '18px', fontWeight: 700, color: '#C2410C' }}>
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
  const { usuario } = useAuth();
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

  // Categoría de ranking — solo Admin/Director (el backend la restringe igual).
  // Se usa el fin del rango seleccionado como mes de referencia (o el mes actual sin rango).
  const fechaRefCategoria = rango?.hasta ? new Date(rango.hasta + 'T00:00:00') : new Date();
  const { data: categoriasData } = useQuery({
    queryKey: ['inf-categorias', fechaRefCategoria.getFullYear(), fechaRefCategoria.getMonth() + 1],
    queryFn:  () => getCategoriasAgentes(fechaRefCategoria.getFullYear(), fechaRefCategoria.getMonth() + 1),
    enabled:  usuario?.rol === 'Admin' || usuario?.rol === 'Director',
  });
  const categoriaPorAgente = useMemo(() => {
    const mapa = new Map<number, CategoriaAgenteDto>();
    categoriasData?.forEach(c => mapa.set(c.agenteID, c));
    return mapa;
  }, [categoriasData]);

  // Líder del mes (cumplimiento de meta) — mismo mes de referencia que la categoría de ranking.
  const { data: informeLideresData } = useQuery({
    queryKey: ['inf-lideres', fechaRefCategoria.getFullYear(), fechaRefCategoria.getMonth() + 1],
    queryFn:  () => getInformeLideres(fechaRefCategoria.getFullYear(), fechaRefCategoria.getMonth() + 1),
    enabled:  usuario?.rol === 'Admin' || usuario?.rol === 'Director',
  });
  const liderDelMes = useMemo(
    () => calcularLiderDelMes(informeLideresData?.liderCarteras ?? []),
    [informeLideresData],
  );

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

  const handleCambiarLider = (val: number | null) => {
    if (val === null) {
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

  const handleCambiarCartera = (val: number | null) => {
    if (val === null) {
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
    border: activo ? '0.5px solid var(--z-brand)' : '0.5px solid var(--z-border)',
    background: activo ? 'var(--z-brand)' : 'var(--z-bg-page)',
    color: activo ? '#FFFFFF' : 'var(--z-text-muted)',
    fontSize: '12px',
    fontWeight: activo ? 600 : 400,
    cursor: 'pointer',
    whiteSpace: 'nowrap',
  });

  return (
    <>
      {/* Líderes destacados del período (solo vista global) — fuera de la columna de
          contenido para ocupar todo el ancho disponible, no solo los 1000px del resto. */}
      {mostrarDesgloseLider && <BarraDestacadosLider filas={data!.filasLider} liderDelMes={liderDelMes} />}

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
        boxShadow: '0 1px 3px rgba(0,0,0,0.06), 0 1px 2px rgba(0,0,0,0.04)',
      }}>
        {/* Fila 1: Líder + Cartera */}
        <div style={{ display: 'flex', gap: '12px', alignItems: 'flex-end', flexWrap: 'wrap' }}>
          <div style={{ display: 'flex', flexDirection: 'column', gap: '4px', minWidth: '200px' }}>
            <label style={{ fontSize: '11px', fontWeight: 600, color: 'var(--z-text-muted)', textTransform: 'uppercase', letterSpacing: '0.04em' }}>
              Líder
            </label>
            <ZDropdown
              options={[{ value: null, label: 'Todos los líderes' }, ...lideres.map(l => ({ value: l.id, label: l.nombre }))]}
              value={liderID}
              onChange={handleCambiarLider}
              background="var(--z-bg-page)"
            />
          </div>

          {liderID !== null && (
            <div style={{ display: 'flex', flexDirection: 'column', gap: '4px', minWidth: '200px' }}>
              <label style={{ fontSize: '11px', fontWeight: 600, color: 'var(--z-text-muted)', textTransform: 'uppercase', letterSpacing: '0.04em' }}>
                Cartera
              </label>
              <ZDropdown
                options={[{ value: null, label: 'Todas las carteras del líder' }, ...carteras.map(c => ({ value: c.id, label: c.nombreCartera }))]}
                value={carteraID}
                onChange={handleCambiarCartera}
                background="var(--z-bg-page)"
              />
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
              valor={formatearPesosCorto(data.metricas.proyeccionPromesas).valor}
              unidad={formatearPesosCorto(data.metricas.proyeccionPromesas).unidad}
              valorReal={formatearPesos(data.metricas.proyeccionPromesas)}
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
            boxShadow: '0 1px 3px rgba(0,0,0,0.06), 0 1px 2px rgba(0,0,0,0.04)',
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
          <h2 style={{
            display: 'flex', alignItems: 'center', justifyContent: 'space-between',
            fontSize: '13px', fontWeight: 600, color: 'var(--z-text-primary)', marginBottom: '12px',
          }}>
            Desglose por asesor
            <button
              type="button"
              onClick={limpiarFiltros}
              title="Volver al desglose por líder"
              aria-label="Volver al desglose por líder"
              style={{
                display: 'flex', alignItems: 'center', gap: '5px',
                borderRadius: '6px', padding: '4px 10px',
                border: '0.5px solid var(--z-border)', background: 'var(--z-bg-page)',
                color: 'var(--z-text-primary)', fontSize: '12px', fontWeight: 500, lineHeight: 1,
                cursor: 'pointer', transition: 'color 0.15s, border-color 0.15s',
              }}
              onMouseOver={(e) => {
                e.currentTarget.style.color = 'var(--z-brand)';
                e.currentTarget.style.borderColor = 'var(--z-brand)';
              }}
              onMouseOut={(e) => {
                e.currentTarget.style.color = 'var(--z-text-primary)';
                e.currentTarget.style.borderColor = 'var(--z-border)';
              }}
            >
              ← Volver
            </button>
          </h2>
          <div style={{
            background: 'var(--z-bg-card)', border: '0.5px solid var(--z-border)',
            borderRadius: '8px', overflow: 'hidden',
            boxShadow: '0 1px 3px rgba(0,0,0,0.06), 0 1px 2px rgba(0,0,0,0.04)',
          }}>
            {isLoading ? (
              <CargandoSpiral mensaje="Cargando asesores..." />
            ) : (
              <TablaAsesores filas={data?.filasAsesor ?? []} categoriaPorAgente={categoriaPorAgente} />
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
    </>
  );
}
