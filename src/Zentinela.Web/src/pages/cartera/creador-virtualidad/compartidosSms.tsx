/* eslint-disable react-refresh/only-export-components */
// Piezas visuales del asistente SMS. Mismo criterio que historico-virtualidad/compartidos.tsx:
// exporta componentes + estilos juntos.
import type { Paso } from './estadoAsistente';

export const C = {
  brand:   '#F54927',
  success: '#16a34a',
  warning: '#d97706',
  danger:  '#dc2626',
  gray:    '#64748b',
  sms:     '#7c3aed',
};

export const inputStyle: React.CSSProperties = {
  width: '100%', padding: '7px 10px', fontSize: 13, border: '1px solid var(--z-border)',
  borderRadius: 6, background: 'var(--z-bg-card)', color: 'var(--z-text-primary)', outline: 'none',
  fontFamily: 'inherit',
};

export const etiquetaPaso: React.CSSProperties = {
  fontSize: 11, fontWeight: 600, color: 'var(--z-text-muted)', textTransform: 'uppercase',
  letterSpacing: '0.04em', margin: 0,
};

const PASOS: { n: Paso; label: string }[] = [
  { n: 1, label: 'Cargar archivo' },
  { n: 2, label: 'Enlaces a distribuir' },
  { n: 3, label: 'Mensaje autorizado' },
  { n: 4, label: 'Generar' },
];

/** Indicador de pasos. Solo se puede volver atrás (a un paso ya completado), nunca saltar adelante. */
export function Stepper({ actual, maxAlcanzado, onIr }: { actual: Paso; maxAlcanzado: Paso; onIr: (p: Paso) => void }) {
  return (
    <div style={{ display: 'flex', alignItems: 'center', gap: 0, flexWrap: 'wrap' }}>
      {PASOS.map((p, i) => {
        const hecho   = p.n < actual;
        const activo  = p.n === actual;
        const clicable = p.n <= maxAlcanzado && !activo;
        return (
          <div key={p.n} style={{ display: 'flex', alignItems: 'center' }}>
            <button
              type="button"
              onClick={() => clicable && onIr(p.n)}
              disabled={!clicable}
              style={{
                display: 'flex', alignItems: 'center', gap: 8, background: 'none', border: 'none',
                cursor: clicable ? 'pointer' : 'default', padding: '4px 6px',
              }}>
              <span style={{
                width: 24, height: 24, borderRadius: '50%', display: 'inline-flex', alignItems: 'center', justifyContent: 'center',
                fontSize: 12, fontWeight: 700,
                background: activo ? C.sms : hecho ? '#ede9fe' : 'var(--z-bg-page)',
                color: activo ? '#fff' : hecho ? C.sms : 'var(--z-text-muted)',
                border: `1px solid ${activo || hecho ? C.sms : 'var(--z-border)'}`,
              }}>
                {hecho ? '✓' : p.n}
              </span>
              <span style={{ fontSize: 13, fontWeight: activo ? 600 : 400, color: activo ? 'var(--z-text-primary)' : 'var(--z-text-muted)' }}>
                {p.label}
              </span>
            </button>
            {i < PASOS.length - 1 && (
              <div style={{ width: 36, height: 1, background: p.n < actual ? C.sms : 'var(--z-border)', margin: '0 4px' }} />
            )}
          </div>
        );
      })}
    </div>
  );
}

export function Aviso({ tipo, children }: { tipo: 'info' | 'warning' | 'danger' | 'success'; children: React.ReactNode }) {
  const color = { info: C.sms, warning: C.warning, danger: C.danger, success: C.success }[tipo];
  const bg    = { info: '#f5f3ff', warning: '#fffbeb', danger: '#fef2f2', success: '#f0fdf4' }[tipo];
  return (
    <div style={{
      background: bg, border: `1px solid ${color}30`, borderLeft: `4px solid ${color}`,
      borderRadius: 8, padding: '10px 14px', fontSize: 13, color: 'var(--z-text-primary)',
    }}>
      {children}
    </div>
  );
}

export function BarraAcciones({ children }: { children: React.ReactNode }) {
  return (
    <div style={{ display: 'flex', gap: 10, justifyContent: 'flex-end', alignItems: 'center', paddingTop: 16, borderTop: '1px solid var(--z-border)' }}>
      {children}
    </div>
  );
}
