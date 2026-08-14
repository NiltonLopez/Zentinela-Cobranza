/* eslint-disable react-refresh/only-export-components */
// Módulo de utilidades compartidas: exporta a propósito componentes + estilos/tipos juntos
// (misma convención que components/informes/reporteCompartido.tsx). El HMR de fast-refresh
// no aplica.
//
// Piezas compartidas por las tres secciones del sub-módulo (Solicitud, Resultado y Carga
// IVR). Viven aparte para que agregar un canal no duplique estilos ni componentes.
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

// Agrupados por mensaje (no una fila por cada registro con el mismo error) y sobre
// TODAS las filas del archivo, no solo la muestra del preview.
export function ListaErroresAgrupados({ errores }: { errores: ErrorAgrupado[] }) {
  if (errores.length === 0) return null;
  return (
    <div style={{ background: '#fef2f2', border: '1px solid #fca5a5', borderRadius: 8, padding: '12px 16px' }}>
      <p style={{ margin: '0 0 8px', fontSize: 12, fontWeight: 600, color: '#b91c1c' }}>
        Errores encontrados — agrupados por tipo
      </p>
      <ul style={{ margin: 0, paddingLeft: 20, display: 'flex', flexDirection: 'column', gap: 4 }}>
        {errores.map((e, i) => (
          <li key={i} style={{ fontSize: 13, color: '#b91c1c' }}>
            {e.mensaje} <strong>({e.cantidad.toLocaleString('es-CO')} fila{e.cantidad !== 1 ? 's' : ''})</strong>
          </li>
        ))}
      </ul>
    </div>
  );
}
