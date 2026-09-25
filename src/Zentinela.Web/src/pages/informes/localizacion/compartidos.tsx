/* eslint-disable react-refresh/only-export-components */
// Piezas compartidas por las pestañas de Informes de Localización (ADR-040): formato, colores,
// chip de estado de fuente, barra de cobertura, podio con fotos, filtros de alcance y avisos.
// Exporta a propósito componentes + helpers juntos (como reporteCompartido); el HMR no aplica.
import { useMemo, type ReactNode } from 'react';
import { useQuery } from '@tanstack/react-query';
import { obtenerOpcionesCalificador } from '../../../api/calificadorApi';
import { useAlcanceLider } from '../../../hooks/useAlcanceLider';
import { AvatarPersona } from '../../../components/AvatarPersona';
import { ZDropdown } from '../../../components/ZDropdown';
import { COLOR_ESTADO_FUENTE, type EstadoFuente } from '../../../types/fuentesLocalizacion';

// ── Formato ───────────────────────────────────────────────────────────────────

export const fmtNum = (v: number | null | undefined) =>
  v == null ? '—' : v.toLocaleString('es-CO');

export const fmtPct = (v: number | null | undefined, dec = 1) =>
  v == null ? '—' : `${v.toLocaleString('es-CO', { minimumFractionDigits: dec, maximumFractionDigits: dec })} %`;

export const fmtFecha = (iso: string | null | undefined) =>
  iso ? new Date(iso).toLocaleDateString('es-CO', { day: '2-digit', month: 'short', year: 'numeric' }) : '—';

export const fmtFechaHora = (iso: string | null | undefined) =>
  iso
    ? new Date(iso).toLocaleString('es-CO', { day: '2-digit', month: 'short', year: 'numeric', hour: '2-digit', minute: '2-digit' })
    : '—';

/** Días desde una fecha ISO hasta hoy (null si no hay fecha). */
export const diasDesde = (iso: string | null | undefined) =>
  iso ? Math.floor((Date.now() - new Date(iso).getTime()) / 86_400_000) : null;

// ── Colores (los mismos del panel "Porcentajes de Localización" del Calificador) ──

export const COLOR_LOC = {
  encontrado:   '#16A34A',
  noEncontrado: '#F59E0B',
  falta:        '#D1D5DB',
  contacto:     '#2563eb',
  cobertura:    '#7c3aed',
  frescura:     '#10b981',
  alerta:       '#dc2626',
} as const;

/** Tramos de frescura: de fresco a viejo. */
export const COLOR_TRAMO = {
  hasta90:   '#16A34A',
  de91a180:  '#84CC16',
  de181a365: '#F59E0B',
  mas365:    '#DC2626',
} as const;

/** Color de un índice o porcentaje "más es mejor": verde ≥ 70, ámbar ≥ 50, rojo < 50. */
export const colorNivel = (v: number | null | undefined) =>
  v == null ? 'var(--z-text-muted)' : v >= 70 ? '#16a34a' : v >= 50 ? '#ca8a04' : '#dc2626';

// ── Chip de estado de fuente ──────────────────────────────────────────────────

export function ChipEstadoFuente({ estado, nota }: { estado: EstadoFuente; nota?: string | null }) {
  const color = COLOR_ESTADO_FUENTE[estado];
  return (
    <span
      title={nota ?? undefined}
      style={{
        fontSize: 10, fontWeight: 700, padding: '2px 6px', borderRadius: 4,
        background: `${color}22`, color, whiteSpace: 'nowrap',
      }}
    >
      {estado}{nota ? ' *' : ''}
    </span>
  );
}

// ── Barra apilada Encontrado / No encontrado / Falta por buscar ───────────────

export function BarraCobertura({ encontrados, noEncontrados, falta, alto = 8, ancho = 140 }: {
  encontrados: number; noEncontrados: number; falta: number; alto?: number; ancho?: number | string;
}) {
  const total = encontrados + noEncontrados + falta;
  const p = (v: number) => (total > 0 ? (v / total) * 100 : 0);
  return (
    <div
      title={`Encontrado ${fmtNum(encontrados)} · No encontrado ${fmtNum(noEncontrados)} · Falta ${fmtNum(falta)}`}
      style={{ height: alto, width: ancho, borderRadius: alto / 2, overflow: 'hidden', display: 'flex', background: 'var(--z-bg-page)' }}
    >
      <div style={{ width: `${p(encontrados)}%`, background: COLOR_LOC.encontrado }} />
      <div style={{ width: `${p(noEncontrados)}%`, background: COLOR_LOC.noEncontrado }} />
      <div style={{ width: `${p(falta)}%`, background: COLOR_LOC.falta }} />
    </div>
  );
}

// ── Variación contra el corte anterior ────────────────────────────────────────

export function Delta({ valor, menorEsMejor = false }: { valor: number | null; menorEsMejor?: boolean }) {
  if (valor == null) return <span style={{ color: 'var(--z-text-muted)', fontSize: 12 }}>—</span>;
  if (valor === 0) return <span style={{ color: 'var(--z-text-muted)', fontSize: 12 }}>= 0,0</span>;
  const mejora = menorEsMejor ? valor < 0 : valor > 0;
  return (
    <span style={{ color: mejora ? '#16a34a' : '#dc2626', fontWeight: 600, fontSize: 12, whiteSpace: 'nowrap' }}>
      {valor > 0 ? '▲' : '▼'} {Math.abs(valor).toLocaleString('es-CO', { minimumFractionDigits: 1, maximumFractionDigits: 1 })}
    </span>
  );
}

// ── Podio (top 3 con foto) ────────────────────────────────────────────────────

export interface PuestoPodio {
  posicion: number;
  id: number;
  nombre: string;
  documento: number | null;
  valor: string;
  detalle?: string;
  esPropio?: boolean;
}

const MEDALLA: Record<number, string> = { 1: '#EAB308', 2: '#94A3B8', 3: '#B45309' };

/** Top 3 en forma de podio (2 · 1 · 3), con la foto grande de cada líder. */
export function Podio({ puestos }: { puestos: PuestoPodio[] }) {
  const top = puestos.filter(p => p.posicion <= 3).slice(0, 3);
  if (top.length === 0) return null;
  // Orden visual clásico: segundo a la izquierda, primero al centro, tercero a la derecha.
  const orden = top.length === 3 ? [top[1], top[0], top[2]] : top;
  const altoBase: Record<number, number> = { 1: 96, 2: 70, 3: 52 };

  return (
    <div style={{ display: 'flex', justifyContent: 'center', alignItems: 'flex-end', gap: 16, flexWrap: 'wrap', padding: '8px 0 0' }}>
      {orden.map(p => {
        const color = MEDALLA[p.posicion] ?? '#94A3B8';
        const tam = p.posicion === 1 ? 112 : 88;
        return (
          <div key={p.id} style={{ display: 'flex', flexDirection: 'column', alignItems: 'center', width: 190, gap: 8 }}>
            <div style={{ position: 'relative' }}>
              <AvatarPersona
                documento={p.documento}
                nombre={p.nombre}
                tam={tam}
                style={{ border: `4px solid ${color}`, boxShadow: '0 4px 12px rgba(0,0,0,0.12)' }}
              />
              <span style={{
                position: 'absolute', bottom: -6, right: -2, width: 28, height: 28, borderRadius: '50%',
                background: color, color: '#fff', fontWeight: 700, fontSize: 13,
                display: 'flex', alignItems: 'center', justifyContent: 'center', border: '2px solid var(--z-bg-card)',
              }}>
                {p.posicion}
              </span>
            </div>
            <div style={{ textAlign: 'center', minHeight: 44 }}>
              <p style={{ margin: 0, fontSize: 13, fontWeight: 600, color: 'var(--z-text-primary)', lineHeight: 1.3 }}>
                {p.nombre}{p.esPropio ? ' (tú)' : ''}
              </p>
              {p.detalle && <p style={{ margin: '2px 0 0', fontSize: 11, color: 'var(--z-text-muted)' }}>{p.detalle}</p>}
            </div>
            <div style={{
              width: '100%', height: altoBase[p.posicion] ?? 52, borderRadius: '8px 8px 0 0',
              background: `linear-gradient(180deg, ${color}33, ${color}11)`, border: `1px solid ${color}55`, borderBottom: 'none',
              display: 'flex', alignItems: 'flex-start', justifyContent: 'center', paddingTop: 10,
            }}>
              <span style={{ fontSize: 20, fontWeight: 700, color: 'var(--z-text-primary)' }}>{p.valor}</span>
            </div>
          </div>
        );
      })}
    </div>
  );
}

// ── Tarjeta de sección ────────────────────────────────────────────────────────

export function Tarjeta({ titulo, derecha, children, refContenido }: {
  titulo?: ReactNode; derecha?: ReactNode; children: ReactNode; refContenido?: React.Ref<HTMLDivElement>;
}) {
  return (
    <div style={{ background: 'var(--z-bg-card)', border: '1px solid var(--z-border)', borderRadius: 10, padding: '18px 16px' }}>
      {(titulo || derecha) && (
        <div style={{ display: 'flex', alignItems: 'center', justifyContent: 'space-between', gap: 12, flexWrap: 'wrap', marginBottom: 14 }}>
          {titulo && <p style={{ fontSize: 13, fontWeight: 600, color: 'var(--z-text-primary)', margin: 0 }}>{titulo}</p>}
          {derecha}
        </div>
      )}
      <div ref={refContenido}>{children}</div>
    </div>
  );
}

/** Botones de opción (estilo de "Ordenar por" de Recaudo). */
export function Opciones<T extends string | number>({ label, opciones, valor, onChange }: {
  label?: string; opciones: { value: T; label: string }[]; valor: T; onChange: (v: T) => void;
}) {
  return (
    <div style={{ display: 'flex', alignItems: 'center', gap: 6, fontSize: 12, flexWrap: 'wrap' }}>
      {label && <span style={{ fontWeight: 600, color: 'var(--z-text-muted)' }}>{label}</span>}
      {opciones.map(o => (
        <button key={String(o.value)} onClick={() => onChange(o.value)} style={{
          padding: '3px 10px', borderRadius: 6, border: '1px solid var(--z-border)',
          background: valor === o.value ? '#ea580c' : 'transparent',
          color: valor === o.value ? '#fff' : 'var(--z-text-muted)',
          cursor: 'pointer', fontSize: 12, fontWeight: valor === o.value ? 600 : 400,
        }}>
          {o.label}
        </button>
      ))}
    </div>
  );
}

// ── Filtros de alcance (Líder / Cartera) ──────────────────────────────────────

export interface Alcance {
  /** 0 = todos (solo Admin/Director). */
  liderID: number;
  /** 0 = todas. */
  carteraID: number;
}

/**
 * Líder y cartera, con las opciones del Calificador (solo relaciones activas). El Líder no ve el
 * selector de líder: el servidor le fija el suyo de todas formas.
 */
export function FiltrosAlcance({ alcance, onChange }: { alcance: Alcance; onChange: (a: Alcance) => void }) {
  const { esLider } = useAlcanceLider();
  const { data: opciones = [] } = useQuery({
    queryKey: ['calificador-opciones'],
    queryFn: obtenerOpcionesCalificador,
    staleTime: 10 * 60 * 1000,
  });

  const { lideres, carteras } = useMemo(() => {
    // Las entradas "Cartera Completo" (liderID null) no aplican: el filtro de cartera ya las cubre.
    const porLider = opciones.filter((o): o is typeof o & { liderID: number } => o.liderID !== null);
    const cmp = (a: { nombre: string }, b: { nombre: string }) => a.nombre.localeCompare(b.nombre, 'es', { sensitivity: 'base' });
    const lideres = Array.from(new Map(porLider.map(o => [o.liderID, {
      id: o.liderID, nombre: o.etiqueta.split(' — ')[1] ?? `Líder ${o.liderID}`,
    }])).values()).sort(cmp);
    const carteras = Array.from(new Map(porLider
      .filter(o => alcance.liderID === 0 || o.liderID === alcance.liderID)
      .map(o => [o.carteraID, { id: o.carteraID, nombre: o.etiqueta.split(' — ')[0] ?? `Cartera ${o.carteraID}` }])).values()).sort(cmp);
    return { lideres, carteras };
  }, [opciones, alcance.liderID]);

  const etiqueta: React.CSSProperties = { fontSize: 12, color: 'var(--z-text-muted)', whiteSpace: 'nowrap' };

  return (
    <div style={{ display: 'flex', gap: 12, flexWrap: 'wrap', alignItems: 'center' }}>
      {!esLider && (
        <div style={{ display: 'flex', alignItems: 'center', gap: 6 }}>
          <span style={etiqueta}>Líder:</span>
          <ZDropdown
            buscable
            ariaLabel="Líder"
            options={[{ value: 0, label: 'Todos los líderes' }, ...lideres.map(l => ({ value: l.id, label: l.nombre }))]}
            value={alcance.liderID}
            onChange={v => onChange({ liderID: v, carteraID: 0 })}
            style={{ minWidth: 220 }}
          />
        </div>
      )}
      <div style={{ display: 'flex', alignItems: 'center', gap: 6 }}>
        <span style={etiqueta}>Cartera:</span>
        <ZDropdown
          buscable
          ariaLabel="Cartera"
          options={[{ value: 0, label: esLider ? 'Todas mis carteras' : 'Todas las carteras' }, ...carteras.map(c => ({ value: c.id, label: c.nombre }))]}
          value={alcance.carteraID}
          onChange={v => onChange({ ...alcance, carteraID: v })}
          style={{ minWidth: 220 }}
        />
      </div>
    </div>
  );
}

/** Texto del universo que se está mirando, para KPIs y PDF. */
export function describirAlcance(alcance: string, etiqueta: string) {
  switch (alcance) {
    case 'par':     return `${etiqueta} (titulares del par)`;
    case 'lider':   return `${etiqueta} (titulares distintos de todas sus carteras)`;
    case 'cartera': return `${etiqueta} (suma de sus líderes activos)`;
    default:        return 'Toda la base (titulares distintos de todos los pares activos)';
  }
}

// ── Lo que no se puede medir ──────────────────────────────────────────────────

export function AvisoNoMedible({ extra }: { extra?: string[] }) {
  const items = [
    'Cuánto lleva cada titular asignado al líder: Titular_Cartera no guarda fecha de asignación.',
    'Envíos a localización anteriores a este módulo: no hay registro.',
    'La frescura real de los cargues legacy: los SP anteriores no refrescaban la fecha cuando el dato no cambiaba (ADR-039).',
    'Cámara Empresa (su clave es el NIT de la empresa, no el titular) y Runt.',
    '"No encontrado" en Claro: la fuente solo trae filas con dato.',
    'El costo o la capacidad del departamento de localización.',
    'El saldo de las carteras cuyo saldo no llega a UltimoEstadoTitular (hoy, entre otras, las Delmar, LATIN y Mil Ambientaria): ahí "sin contacto con saldo" aparece como —.',
    ...(extra ?? []),
  ];
  return (
    <details style={{
      background: 'var(--z-bg-page)', border: '1px solid var(--z-border)', borderRadius: 8,
      padding: '10px 14px', fontSize: 12, color: 'var(--z-text-muted)',
    }}>
      <summary style={{ cursor: 'pointer', fontWeight: 600 }}>Qué no se puede medir hoy</summary>
      <ul style={{ margin: '8px 0 0', paddingLeft: 18, display: 'flex', flexDirection: 'column', gap: 4 }}>
        {items.map(t => <li key={t}>{t}</li>)}
      </ul>
    </details>
  );
}

// ── Estilos de tabla compactos ────────────────────────────────────────────────

export const TD_NUM: React.CSSProperties = { textAlign: 'right', fontVariantNumeric: 'tabular-nums' };
