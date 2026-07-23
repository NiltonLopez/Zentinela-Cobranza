import { useState, useMemo } from 'react';
import { useQuery } from '@tanstack/react-query';
import {
  obtenerDashboardLider,
  obtenerCarterasLiderPropias,
} from '../../api/dashboardApi';
import { getInformeLideres } from '../../api/informesRecaudoApi';
import type { FilaAsesorDto, MetricasDirectorDto } from '../../types/dashboard';
import { useAuth } from '../../context/AuthContext';
import { CargandoSpiral } from '../../components/CargandoSpiral';
import { ZDropdown } from '../../components/ZDropdown';
import { ThOrdenable } from '../../components/ThOrdenable';
import { useSort } from '../../hooks/useSort';
import { formatearTiempoTranscurrido } from '../../utils/formatters';
import { MARGEN_BARRA_COMPLETA } from '../../utils/layout';
import { calcularLiderDelMes, type LiderDelMes } from '../../utils/liderDelMes';
import { IconoTrofeo, IconoBolsaDinero, IconoEscalaSubiendo } from '../../components/IconosLiderDelMes';

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
  const millones = Math.trunc(valor / 1_000_000);
  return { valor: `$ ${millones.toLocaleString('es-CO')}`, unidad: 'millones' };
}

function formatearFechaCorta(iso: string): string {
  const [y, m, d] = iso.split('-');
  return `${d}/${m}/${y}`;
}

// ── Presets ───────────────────────────────────────────────────────────────────

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

// ── Barra de frase motivadora ─────────────────────────────────────────────────
// Frases seleccionadas según el % de avance de gestión del equipo, en un ticker
// que se desplaza igual que BarraDestacadosLider (DashboardDirectorPage.tsx).

const RANGOS_FRASE: { min: number; max: number; frases: string[]; fraseCierre?: string[] }[] = [
  {
    min: 0, max: 40,
    frases: [
      'Tu liderazgo marca el ritmo del equipo.',
      'Tu equipo cuenta con tu guía para superarlos.',
      'Vamos con todo, aún hay camino por recorrer.',
      'Tu equipo te necesita enfocado hoy.',
    ],
  },
  {
    min: 40, max: 60,
    frases: [
      'Tu equipo ya recorrió la mitad del camino, se nota tu acompañamiento.',
      'Confía en el talento de tu equipo.',
      'La constancia que le has transmitido a tu equipo los ha traído hasta aquí.',
      'Buen progreso del equipo hasta ahora, sigue impulsándolos.',
      'Tu equipo avanza, no bajes el ritmo.',
    ],
  },
  {
    min: 60, max: 80,
    frases: [
      'Tu liderazgo se siente en el resultado.',
      'Mantén ese gran ritmo hoy, ayuda a tu equipo a asegurar la meta.',
      'Ya casi lo logran, ¡con todo!',
    ],
  },
  {
    min: 80, max: 100,
    frases: [
      'Gracias por tu liderazgo y por la entrega que le contagiaste a tu equipo.',
      '¡Excelente gestión de equipo!',
      'A cerrar con broche de oro.',
    ],
    // Solo se agrega dentro de los últimos días del mes calendario.
    fraseCierre: ['Es momento de celebrar con tu equipo el cierre de mes.'],
  },
];

function esCierreDeMes(): boolean {
  const hoy = new Date();
  const ultimoDiaMes = new Date(hoy.getFullYear(), hoy.getMonth() + 1, 0).getDate();
  return hoy.getDate() >= ultimoDiaMes - 2;
}

interface ItemFrase {
  texto: string;
  icono?: React.ReactNode;
}

function fraseLiderDelMes(liderDelMes: LiderDelMes, liderID: number): ItemFrase {
  const esUsuarioActual = liderDelMes.liderID === liderID;

  if (liderDelMes.tipo === 'cumplimiento') {
    const porcentaje = (liderDelMes.valor * 100).toFixed(1);
    return {
      icono: <IconoTrofeo size={20} />,
      texto: esUsuarioActual
        ? `¡Eres el líder actual del mes! ${porcentaje}% de cumplimiento de meta.`
        : `Líder actual del mes: ${liderDelMes.nombreLider} con ${porcentaje}% de cumplimiento de meta.`,
    };
  }

  // Plan B: sin metas cargadas ese mes (o todas en 0%) — se destacan honorarios totales y productividad por separado.
  const valor = formatearPesos(liderDelMes.valor);
  if (liderDelMes.tipo === 'honorarios-totales') {
    return {
      icono: <IconoBolsaDinero size={20} />,
      texto: esUsuarioActual
        ? `¡Tienes más honorarios recaudados este mes! ${valor}.`
        : `Más honorarios recaudados: ${liderDelMes.nombreLider} con ${valor}.`,
    };
  }
  return {
    icono: <IconoEscalaSubiendo size={20} />,
    texto: esUsuarioActual
      ? `¡Tienes la mejor productividad este mes! ${valor} en honorarios por asesor.`
      : `Mejor productividad: ${liderDelMes.nombreLider} con ${valor} en honorarios por asesor.`,
  };
}

function BarraFraseLider({ pct, liderDelMes, liderID }: { pct: number; liderDelMes: LiderDelMes[]; liderID: number }) {
  const frases = useMemo<ItemFrase[]>(() => {
    const rango = RANGOS_FRASE.find(r => pct >= r.min && pct <= r.max) ?? RANGOS_FRASE[0];
    const base: ItemFrase[] = rango.frases.map(texto => ({ texto }));
    if (rango.fraseCierre && esCierreDeMes()) base.push(...rango.fraseCierre.map(texto => ({ texto })));
    liderDelMes.forEach(item => base.push(fraseLiderDelMes(item, liderID)));
    return base;
  }, [pct, liderDelMes, liderID]);

  const [pausado, setPausado] = useState(false);

  if (frases.length === 0) return null;

  // Pista duplicada: al recorrer el 50% del ancho total vuelve a verse igual → loop continuo sin salto.
  const pista = [...frases, ...frases];

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
        @keyframes zentinelaFraseLectura {
          from { transform: translateX(0%); }
          to   { transform: translateX(-50%); }
        }
      `}</style>
      <div style={{
        display: 'flex', width: 'max-content',
        animationName: 'zentinelaFraseLectura',
        animationDuration: '28s',
        animationTimingFunction: 'linear',
        animationIterationCount: 'infinite',
        animationPlayState: pausado ? 'paused' : 'running',
      }}>
        {pista.map((item, i) => (
          <span key={i} style={{
            display: 'inline-flex', alignItems: 'center', gap: '6px',
            fontSize: '15px', fontWeight: 600, color: '#fff',
            padding: '0 24px', borderRight: '1px solid rgba(255,255,255,0.3)',
            whiteSpace: 'nowrap',
          }}>
            {item.icono}
            {item.texto}
          </span>
        ))}
      </div>
    </div>
  );
}

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
      boxShadow: '0 1px 3px rgba(0,0,0,0.06), 0 1px 2px rgba(0,0,0,0.04)',
    }}>
      <p style={{ fontSize: '11px', fontWeight: 600, color: 'var(--z-text-muted)', textTransform: 'uppercase', letterSpacing: '0.04em', marginBottom: '10px' }}>
        {label}
      </p>
      <p style={{ fontSize: `${valorSize}px`, fontWeight: 700, color: 'var(--z-text-primary)', lineHeight: 1, whiteSpace: 'nowrap' }}>
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
          <ThOrdenable label="Asesor"          sortKey="nombreAsesor"        sort={sort} onSort={toggle} resaltado />
          <ThOrdenable label="Gestiones"       sortKey="totalGestiones"      sort={sort} onSort={toggle} align="right" resaltado />
          <ThOrdenable label="Contactabilidad" sortKey="tasaContactabilidad" sort={sort} onSort={toggle} align="right" resaltado />
          <ThOrdenable label="Efectividad"     sortKey="tasaEfectividad"     sort={sort} onSort={toggle} align="right" resaltado />
          <ThOrdenable label="Proyección"      sortKey="proyeccionPromesas"  sort={sort} onSort={toggle} align="right" resaltado />
        </tr>
      </thead>
      <tbody>
        {items.map(f => (
          <tr
            key={f.agenteID}
            style={{ borderBottom: '0.5px solid var(--z-border)' }}
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

export function DashboardLiderPage() {
  const { usuario } = useAuth();
  const liderID = usuario?.liderID ?? null;

  const [carteraID,     setCarteraID]     = useState<number | null>(null);
  const [carteraNombre, setCarteraNombre] = useState<string>('');
  const [modalExcluidosAbierto, setModalExcluidosAbierto] = useState(false);

  const [preset,      setPreset]      = useState<Preset>('ciclo');
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

  // Líder del mes (cumplimiento de meta) — siempre el mes calendario en curso,
  // independiente del preset de fechas (que solo afecta las métricas de gestión).
  const hoy = new Date();
  const { data: informeLideresData } = useQuery({
    queryKey: ['inf-lideres-mes-actual', hoy.getFullYear(), hoy.getMonth() + 1],
    queryFn:  () => getInformeLideres(hoy.getFullYear(), hoy.getMonth() + 1),
    enabled:  liderID !== null,
    staleTime: 5 * 60 * 1000,
  });
  const liderDelMes = useMemo(
    () => calcularLiderDelMes(informeLideresData?.liderCarteras ?? []),
    [informeLideresData],
  );

  const tituloFiltro = carteraNombre || 'Todas mis carteras';

  const pctAvance = data && data.metricas.totalAsignados > 0
    ? Math.min((data.metricas.totalGestiones / data.metricas.totalAsignados) * 100, 100)
    : 0;

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

  if (!liderID) {
    return (
      <div style={{ padding: '48px', textAlign: 'center', fontSize: '13px', color: 'var(--z-text-muted)' }}>
        Tu cuenta no tiene un líder asociado.
      </div>
    );
  }

  return (
    <>
      {data && <BarraFraseLider pct={pctAvance} liderDelMes={liderDelMes} liderID={liderID} />}

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
            <ZDropdown
              options={[{ value: null, label: 'Todas mis carteras' }, ...carteras.map(c => ({ value: c.id, label: c.nombreCartera }))]}
              value={carteraID}
              onChange={handleCambiarCartera}
              background="var(--z-bg-page)"
            />
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
