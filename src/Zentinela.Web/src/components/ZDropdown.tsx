import { useEffect, useMemo, useRef, useState } from 'react';

export interface ZDropdownOption<T> {
  value: T;
  label: string;
  sublabel?: string;
}

interface Props<T> {
  options: ZDropdownOption<T>[];
  value: T;
  onChange: (value: T) => void;
  style?: React.CSSProperties;
  background?: string;
  disabled?: boolean;
}

const FUENTE_MEDICION = '600 13px -apple-system, BlinkMacSystemFont, "Segoe UI", "Inter", sans-serif';
let canvasMedicion: HTMLCanvasElement | null = null;

// Ancho (px) de la etiqueta más larga, midiendo el texto real en vez de
// asumir un ancho fijo — evita que options.label largas queden truncadas.
function medirEtiquetaMasLarga(labels: string[]): number {
  canvasMedicion ??= document.createElement('canvas');
  const ctx = canvasMedicion.getContext('2d');
  if (!ctx) return 0;
  ctx.font = FUENTE_MEDICION;
  return Math.max(0, ...labels.map((l) => ctx.measureText(l).width));
}

function aPx(valor: React.CSSProperties['minWidth']): number {
  if (typeof valor === 'number') return valor;
  if (typeof valor === 'string') {
    const n = parseFloat(valor);
    return Number.isNaN(n) ? 0 : n;
  }
  return 0;
}

// Dropdown propio (botón + panel flotante) para reemplazar al <select> nativo,
// cuya lista de opciones no se puede estilizar. Mismo patrón visual que el
// selector de cartera de AuditoriaGestionesPage.
export function ZDropdown<T>({ options, value, onChange, style, background = 'var(--z-bg-card)', disabled = false }: Props<T>) {
  const [abierto, setAbierto] = useState(false);
  const ref = useRef<HTMLDivElement>(null);

  const { minWidth: minWidthEstilo, ...restoStyle } = style ?? {};

  // Padding horizontal del botón (7px*2) + gap (10) + ancho de flecha (~10) + borde.
  const anchoTexto = useMemo(
    () => medirEtiquetaMasLarga(options.map((o) => o.label)),
    [options],
  );
  const anchoMinimoContenido = Math.ceil(anchoTexto) + 20 + 10 + 10 + 2;
  // Tope al ancho auto-calculado: con etiquetas muy largas (p. ej. "cartera —
  // líder") el botón no debe crecer sin límite; el texto se trunca con
  // ellipsis y el detalle completo queda visible en el panel desplegado.
  const ANCHO_MAXIMO_CALCULADO = 340;
  const anchoMinimo = Math.max(Math.min(anchoMinimoContenido, ANCHO_MAXIMO_CALCULADO), aPx(minWidthEstilo));

  useEffect(() => {
    if (!abierto) return;
    const alClic = (e: MouseEvent) => {
      if (ref.current && !ref.current.contains(e.target as Node)) setAbierto(false);
    };
    document.addEventListener('mousedown', alClic);
    return () => document.removeEventListener('mousedown', alClic);
  }, [abierto]);

  const seleccionado = options.find(o => o.value === value) ?? null;

  return (
    <div ref={ref} style={{ position: 'relative', width: 'fit-content', ...restoStyle, minWidth: anchoMinimo }}>
      <button
        type="button"
        disabled={disabled}
        onClick={() => setAbierto(o => !o)}
        style={{
          width: '100%', display: 'flex', alignItems: 'center', justifyContent: 'space-between', gap: 10,
          padding: '7px 10px', fontSize: 13, borderRadius: 6, textAlign: 'left',
          border: `0.5px solid ${abierto ? 'var(--z-brand)' : 'var(--z-border)'}`,
          background, cursor: disabled ? 'not-allowed' : 'pointer', outline: 'none',
          opacity: disabled ? 0.6 : 1,
          boxShadow: abierto ? '0 0 0 3px var(--z-brand-light)' : 'none',
          transition: 'border-color 0.15s ease, box-shadow 0.15s ease',
        }}
      >
        <span style={{
          color: seleccionado ? 'var(--z-text-primary)' : 'var(--z-text-muted)',
          fontWeight: seleccionado ? 600 : 400,
          whiteSpace: 'nowrap', overflow: 'hidden', textOverflow: 'ellipsis',
        }}>
          {seleccionado ? seleccionado.label : ''}
        </span>
        <span style={{
          color: 'var(--z-text-muted)', fontSize: 10, flexShrink: 0,
          transform: abierto ? 'rotate(180deg)' : 'none', transition: 'transform .15s ease',
        }}>
          ▼
        </span>
      </button>

      {abierto && (
        <div style={{
          position: 'absolute', top: 'calc(100% + 6px)', left: 0, zIndex: 20,
          minWidth: '100%', width: 'max-content', maxWidth: 420,
          background: 'var(--z-bg-card)', border: '0.5px solid var(--z-border)', borderRadius: 8,
          boxShadow: '0 8px 24px rgba(0,0,0,0.12)', maxHeight: 320, overflowY: 'auto', padding: 4,
        }}>
          {options.map((o, i) => {
            const sel = o.value === value;
            return (
              <button
                key={i}
                type="button"
                onClick={() => { onChange(o.value); setAbierto(false); }}
                onMouseEnter={(e) => {
                  if (sel) return;
                  e.currentTarget.style.background = 'var(--z-brand-light)';
                  const label = e.currentTarget.querySelector<HTMLSpanElement>('.z-dropdown-label');
                  if (label) label.style.color = 'var(--z-brand)';
                }}
                onMouseLeave={(e) => {
                  if (sel) return;
                  e.currentTarget.style.background = 'transparent';
                  const label = e.currentTarget.querySelector<HTMLSpanElement>('.z-dropdown-label');
                  if (label) label.style.color = 'var(--z-text-primary)';
                }}
                style={{
                  width: '100%', textAlign: 'left', display: 'flex', flexDirection: 'column', gap: 2,
                  padding: '9px 12px', borderRadius: 6, border: 'none', cursor: 'pointer',
                  background: sel ? 'var(--z-brand-light)' : 'transparent', transition: 'background .12s ease',
                }}
              >
                <span className="z-dropdown-label" style={{ fontSize: 13, fontWeight: 600, color: sel ? 'var(--z-brand)' : 'var(--z-text-primary)', whiteSpace: 'nowrap' }}>
                  {o.label}
                </span>
                {o.sublabel && (
                  <span style={{ fontSize: 11, color: 'var(--z-text-muted)', lineHeight: 1.35 }}>
                    {o.sublabel}
                  </span>
                )}
              </button>
            );
          })}
        </div>
      )}
    </div>
  );
}
