/* eslint-disable react-refresh/only-export-components */
// Módulo de utilidades compartidas: exporta a propósito componentes + estilos/tipos juntos
// (misma convención que components/informes/reporteCompartido.tsx). El HMR de fast-refresh
// no aplica.
//
// Piezas compartidas por las tres secciones del sub-módulo (Solicitud, Resultado y Carga
// IVR). Viven aparte para que agregar un canal no duplique estilos ni componentes.
import { useState } from 'react';
import type { ErrorAgrupado } from '../../../types/historicoVirtualidad';

export type Etapa = 'inicial' | 'preview' | 'resultado';

// ── Estilos de tabla ────────────────────────────────────────────────────────

export const TH: React.CSSProperties = {
  padding: '8px 10px', textAlign: 'left', fontSize: 11, fontWeight: 600,
  color: 'var(--z-text-muted)', borderBottom: '0.5px solid var(--z-border)',
  letterSpacing: '0.04em', textTransform: 'uppercase', whiteSpace: 'nowrap',
};

export const TD: React.CSSProperties = {
  padding: '8px 10px', fontSize: 13, borderBottom: '0.5px solid var(--z-border)',
  whiteSpace: 'nowrap', color: 'var(--z-text-primary)',
};

// ── Botones y cajas ─────────────────────────────────────────────────────────

export const btnPrimario = (habilitado: boolean): React.CSSProperties => ({
  padding: '9px 20px', borderRadius: 6, border: 'none',
  background: habilitado ? 'var(--z-brand)' : 'var(--z-border)',
  color: habilitado ? '#fff' : 'var(--z-text-muted)',
  fontSize: 13, fontWeight: 600, cursor: habilitado ? 'pointer' : 'not-allowed',
});

export const btnSecundario: React.CSSProperties = {
  padding: '9px 16px', borderRadius: 6, border: '0.5px solid var(--z-border)',
  background: 'var(--z-bg-page)', color: 'var(--z-text-muted)', fontSize: 13, cursor: 'pointer',
};

export const cajaError: React.CSSProperties = {
  background: '#fef2f2', border: '0.5px solid #fca5a5', borderRadius: 6,
  padding: '10px 14px', fontSize: 13, color: '#dc2626',
};

// ── Chip de conteo (clicable para desplegar detalle) ────────────────────────

export function ChipConteo({ numero, label, bg, border, colorNum, colorLabel, onClick, abierto }: {
  numero: number; label: string; bg: string; border: string;
  colorNum: string; colorLabel: string; onClick?: () => void; abierto?: boolean;
}) {
  const clickeable = !!onClick;
  return (
    <div
      onClick={onClick}
      role={clickeable ? 'button' : undefined}
      tabIndex={clickeable ? 0 : undefined}
      style={{
        display: 'flex', alignItems: 'center', gap: 8, background: bg,
        border: `0.5px solid ${border}`, borderRadius: 8, padding: '12px 18px',
        cursor: clickeable ? 'pointer' : 'default', userSelect: 'none',
        boxShadow: clickeable && abierto ? `inset 0 0 0 1px ${border}` : 'none',
      }}
    >
      <span style={{ fontSize: 22, fontWeight: 700, color: colorNum }}>
        {numero.toLocaleString('es-CO')}
      </span>
      <span style={{ fontSize: 13, color: colorLabel }}>{label}</span>
      {clickeable && (
        <span style={{ fontSize: 11, color: colorLabel, marginLeft: 4, transform: abierto ? 'rotate(180deg)' : 'none' }}>▾</span>
      )}
    </div>
  );
}

// ── Zona de carga de archivo ────────────────────────────────────────────────

export function DropZone({
  archivoNombre, archivoKB, onClick, onQuitar, inputRef, onChange, disabled,
  accept = '.xlsx', textoVacio,
}: {
  archivoNombre: string; archivoKB: number; onClick: () => void; onQuitar: () => void;
  inputRef: React.RefObject<HTMLInputElement | null>;
  onChange: (e: React.ChangeEvent<HTMLInputElement>) => void;
  disabled?: boolean;
  /** Extensiones aceptadas; el canal las define (IVR admite CSV además de XLSX). */
  accept?: string;
  textoVacio?: string;
}) {
  return (
    <div style={{ display: 'flex', flexDirection: 'column', gap: 6 }}>
      <div
        onClick={() => !disabled && onClick()}
        style={{
          border: `1.5px dashed ${archivoNombre ? 'var(--z-brand)' : 'var(--z-border)'}`,
          borderRadius: 8, padding: 24, textAlign: 'center',
          cursor: disabled ? 'not-allowed' : 'pointer',
          background: archivoNombre ? '#eff6ff' : 'transparent', opacity: disabled ? 0.5 : 1,
        }}
      >
        <p style={{ fontSize: 13, color: archivoNombre ? 'var(--z-brand)' : 'var(--z-text-muted)', margin: '0 0 4px' }}>
          {archivoNombre || textoVacio || `Haz clic para seleccionar un archivo ${accept}`}
        </p>
        {archivoNombre && (
          <p style={{ fontSize: 11, color: 'var(--z-text-muted)', margin: 0 }}>{archivoKB.toFixed(1)} KB</p>
        )}
      </div>
      <input ref={inputRef} type="file" accept={accept} disabled={disabled} onChange={onChange} style={{ display: 'none' }} />
      {archivoNombre && (
        <button
          onClick={onQuitar}
          style={{ background: 'none', border: 'none', fontSize: 11, color: 'var(--z-text-muted)', cursor: 'pointer', padding: 0, width: 'fit-content' }}
        >
          × Quitar archivo
        </button>
      )}
    </div>
  );
}

// ── Errores agrupados ───────────────────────────────────────────────────────

// Paleta por tono. 'error' conserva EXACTAMENTE los colores que siempre tuvo
// ListaErroresAgrupados (la usan otras páginas); 'aviso' e 'info' son sus variantes.
const TONOS: Record<'error' | 'aviso' | 'info', { bg: string; borde: string; texto: string }> = {
  error: { bg: '#fef2f2',           borde: '#fca5a5',        texto: '#b91c1c' },
  aviso: { bg: '#fffbeb',           borde: '#fcd34d',        texto: '#d97706' },
  info:  { bg: 'var(--z-bg-page)',  borde: 'var(--z-border)', texto: '#64748b' },
};

// Agrupados por mensaje (no una fila por cada registro con el mismo error) y sobre
// TODAS las filas del archivo, no solo la muestra del preview.
//
// Cada grupo que traiga `ejemplos` se puede desplegar para ver filas concretas: un conteo
// solo ("312 con cédula inválida") obliga a abrir el archivo y adivinar cuáles fueron. Los
// grupos sin ejemplos se pintan igual que siempre, así que los módulos que no los mandan
// no cambian.
//
// `filtrarMotivo` deja reusar el mismo componente para un solo indicador, que es lo que
// necesita cada chip de conteo al desplegarse.
export function ListaAgrupada({ titulo, items, tono, filtrarMotivo }: {
  titulo: string; items: ErrorAgrupado[]; tono: 'error' | 'aviso' | 'info'; filtrarMotivo?: string;
}) {
  const visibles = filtrarMotivo ? items.filter(e => e.motivo === filtrarMotivo) : items;
  if (visibles.length === 0) return null;
  const t = TONOS[tono];
  return (
    <div style={{ background: t.bg, border: `1px solid ${t.borde}`, borderRadius: 8, padding: '12px 16px' }}>
      <p style={{ margin: '0 0 8px', fontSize: 12, fontWeight: 600, color: t.texto }}>
        {titulo}
      </p>
      <ul style={{ margin: 0, padding: 0, listStyle: 'none', display: 'flex', flexDirection: 'column', gap: 6 }}>
        {visibles.map((e, i) => <GrupoAgrupado key={i} item={e} color={t.texto} borde={t.borde} />)}
      </ul>
    </div>
  );
}

function GrupoAgrupado({ item, color, borde }: { item: ErrorAgrupado; color: string; borde: string }) {
  const [abierto, setAbierto] = useState(false);
  const ejemplos = item.ejemplos ?? [];
  const desplegable = ejemplos.length > 0;
  const alternar = () => setAbierto(a => !a);

  return (
    <li style={{ fontSize: 13, color }}>
      <div
        onClick={desplegable ? alternar : undefined}
        role={desplegable ? 'button' : undefined}
        tabIndex={desplegable ? 0 : undefined}
        onKeyDown={desplegable ? ev => { if (ev.key === 'Enter' || ev.key === ' ') { ev.preventDefault(); alternar(); } } : undefined}
        style={{ display: 'flex', alignItems: 'baseline', gap: 6, cursor: desplegable ? 'pointer' : 'default' }}
        title={desplegable ? (abierto ? 'Ocultar el detalle' : 'Ver filas de ejemplo') : undefined}
      >
        <span style={{ width: 12, flexShrink: 0, fontSize: 10, display: 'inline-block', transform: abierto ? 'rotate(90deg)' : 'none', transition: 'transform 0.15s ease' }}>
          {desplegable ? '▶' : '•'}
        </span>
        <span>
          {item.mensaje} <strong>({item.cantidad.toLocaleString('es-CO')} fila{item.cantidad !== 1 ? 's' : ''})</strong>
        </span>
      </div>
      {abierto && (
        <div style={{ margin: '4px 0 2px 18px', padding: '6px 10px', background: 'var(--z-bg-card)', border: `0.5px solid ${borde}`, borderRadius: 6 }}>
          {ejemplos.map((ej, k) => (
            <div key={k} style={{ fontSize: 12, fontFamily: 'ui-monospace, Menlo, monospace' }}>{ej}</div>
          ))}
          {item.cantidad > ejemplos.length && (
            <div style={{ fontSize: 11, marginTop: 4, opacity: 0.8 }}>
              …y {(item.cantidad - ejemplos.length).toLocaleString('es-CO')} más.
            </div>
          )}
        </div>
      )}
    </li>
  );
}

export function ListaErroresAgrupados({ errores, titulo, filtrarMotivo }: {
  errores: ErrorAgrupado[]; titulo?: string; filtrarMotivo?: string;
}) {
  return (
    <ListaAgrupada
      titulo={titulo ?? 'Errores encontrados — agrupados por tipo'}
      items={errores}
      tono="error"
      filtrarMotivo={filtrarMotivo}
    />
  );
}
