import { useEffect, useId, useMemo, useRef, useState } from 'react';

export interface ZDropdownOption<T> {
  value: T;
  label: string;
  sublabel?: string;
  /**
   * Texto adicional (no visible) con el que también se encuentra la opción cuando el
   * dropdown es `buscable` — p. ej. la clave técnica de una fuente ("NuevaEps").
   */
  textoBusqueda?: string;
}

interface Props<T> {
  options: ZDropdownOption<T>[];
  value: T;
  onChange: (value: T) => void;
  style?: React.CSSProperties;
  background?: string;
  disabled?: boolean;
  /**
   * Permite escribir para filtrar las opciones (por etiqueta y `textoBusqueda`, sin tildes
   * ni mayúsculas). Cerrado se ve IGUAL que el dropdown normal; al abrirlo, el texto del
   * botón pasa a ser un campo de búsqueda. Por defecto false: las demás páginas no cambian.
   */
  buscable?: boolean;
  /** Nombre accesible del campo cuando es `buscable` (no hay <label> visible). */
  ariaLabel?: string;
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

// Ancho mínimo del contenedor y el resto del estilo recibido (común a ambas variantes).
function useAnchoMinimo<T>(options: ZDropdownOption<T>[], style: React.CSSProperties | undefined) {
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
  return { restoStyle, anchoMinimo };
}

// ── Estilos compartidos por las dos variantes (mismo aspecto) ───────────────

const estiloDisparador = (abierto: boolean, background: string, disabled: boolean): React.CSSProperties => ({
  width: '100%', display: 'flex', alignItems: 'center', justifyContent: 'space-between', gap: 10,
  padding: '7px 10px', fontSize: 13, borderRadius: 6, textAlign: 'left',
  border: `0.5px solid ${abierto ? 'var(--z-brand)' : 'var(--z-border)'}`,
  background, cursor: disabled ? 'not-allowed' : 'pointer', outline: 'none',
  opacity: disabled ? 0.6 : 1,
  boxShadow: abierto ? '0 0 0 3px var(--z-brand-light)' : 'none',
  transition: 'border-color 0.15s ease, box-shadow 0.15s ease',
});

const estiloFlecha = (abierto: boolean): React.CSSProperties => ({
  color: 'var(--z-text-muted)', fontSize: 10, flexShrink: 0,
  transform: abierto ? 'rotate(180deg)' : 'none', transition: 'transform .15s ease',
});

const ESTILO_PANEL: React.CSSProperties = {
  position: 'absolute', top: 'calc(100% + 6px)', left: 0, zIndex: 20,
  minWidth: '100%', width: 'max-content', maxWidth: 420,
  background: 'var(--z-bg-card)', border: '0.5px solid var(--z-border)', borderRadius: 8,
  boxShadow: '0 8px 24px rgba(0,0,0,0.12)', maxHeight: 320, overflowY: 'auto', padding: 4,
};

const estiloOpcion = (activa: boolean): React.CSSProperties => ({
  width: '100%', textAlign: 'left', display: 'flex', flexDirection: 'column', gap: 2,
  padding: '9px 12px', borderRadius: 6, border: 'none', cursor: 'pointer',
  background: activa ? 'var(--z-brand-light)' : 'transparent', transition: 'background .12s ease',
});

const estiloEtiquetaOpcion = (activa: boolean): React.CSSProperties => ({
  fontSize: 13, fontWeight: 600, color: activa ? 'var(--z-brand)' : 'var(--z-text-primary)', whiteSpace: 'nowrap',
});

const ESTILO_SUBLABEL: React.CSSProperties = { fontSize: 11, color: 'var(--z-text-muted)', lineHeight: 1.35 };

// Dropdown propio (botón + panel flotante) para reemplazar al <select> nativo,
// cuya lista de opciones no se puede estilizar. Mismo patrón visual que el
// selector de cartera de AuditoriaGestionesPage.
export function ZDropdown<T>(props: Props<T>) {
  return props.buscable ? <ZDropdownBuscable {...props} /> : <ZDropdownSimple {...props} />;
}

function ZDropdownSimple<T>({ options, value, onChange, style, background = 'var(--z-bg-card)', disabled = false }: Props<T>) {
  const [abierto, setAbierto] = useState(false);
  const ref = useRef<HTMLDivElement>(null);
  const { restoStyle, anchoMinimo } = useAnchoMinimo(options, style);

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
        style={estiloDisparador(abierto, background, disabled)}
      >
        <span style={{
          color: seleccionado ? 'var(--z-text-primary)' : 'var(--z-text-muted)',
          fontWeight: seleccionado ? 600 : 400,
          whiteSpace: 'nowrap', overflow: 'hidden', textOverflow: 'ellipsis',
        }}>
          {seleccionado ? seleccionado.label : ''}
        </span>
        <span style={estiloFlecha(abierto)}>
          ▼
        </span>
      </button>

      {abierto && (
        <div style={ESTILO_PANEL}>
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
                style={estiloOpcion(sel)}
              >
                <span className="z-dropdown-label" style={estiloEtiquetaOpcion(sel)}>
                  {o.label}
                </span>
                {o.sublabel && (
                  <span style={ESTILO_SUBLABEL}>
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

// ── Variante con búsqueda ───────────────────────────────────────────────────

/** Minúsculas y sin tildes, para comparar lo que se escribe con las opciones. */
function normalizar(s: string) {
  return s.normalize('NFD').replace(/[̀-ͯ]/g, '').toLowerCase().trim();
}

/**
 * Combobox (patrón ARIA "combobox + listbox") con el MISMO aspecto del dropdown normal: se
 * conserva el recuadro del botón y su texto pasa a ser un <input>. Cerrado muestra la opción
 * elegida; al abrirlo el campo queda vacío (la opción elegida queda como placeholder) y lo
 * que se escribe filtra la lista. Teclado: ↓/↑ recorren, Enter elige, Escape cierra sin
 * cambiar. El foco no sale del input: la opción resaltada se anuncia con aria-activedescendant.
 */
function ZDropdownBuscable<T>({ options, value, onChange, style, background = 'var(--z-bg-card)', disabled = false, ariaLabel }: Props<T>) {
  const [abierto, setAbierto]     = useState(false);
  const [texto, setTexto]         = useState('');
  const [resaltado, setResaltado] = useState(0);
  const ref      = useRef<HTMLDivElement>(null);
  const inputRef = useRef<HTMLInputElement>(null);
  const panelRef = useRef<HTMLDivElement>(null);
  const idBase   = useId();
  const idLista  = `${idBase}-lista`;
  const idOpcion = (i: number) => `${idBase}-op-${i}`;
  const { restoStyle, anchoMinimo } = useAnchoMinimo(options, style);

  const seleccionado = options.find(o => o.value === value) ?? null;
  const q = normalizar(texto);
  const visibles = q
    ? options.filter(o => normalizar(`${o.label} ${o.textoBusqueda ?? ''}`).includes(q))
    : options;
  // Al acotar la lista el índice resaltado puede quedar fuera de rango: se recorta al pintar.
  const activo = Math.min(resaltado, visibles.length - 1);

  const cerrar = () => { setAbierto(false); setTexto(''); };
  const abrir = () => {
    if (disabled || abierto) return;
    setTexto('');
    setResaltado(Math.max(0, options.findIndex(o => o.value === value)));
    setAbierto(true);
  };
  const elegir = (o: ZDropdownOption<T>) => { onChange(o.value); cerrar(); };

  useEffect(() => {
    if (!abierto) return;
    const alClic = (e: MouseEvent) => {
      if (ref.current && !ref.current.contains(e.target as Node)) { setAbierto(false); setTexto(''); }
    };
    document.addEventListener('mousedown', alClic);
    return () => document.removeEventListener('mousedown', alClic);
  }, [abierto]);

  // Con el teclado, la opción resaltada se mantiene visible desplazando solo el panel (no la página).
  useEffect(() => {
    const panel = panelRef.current;
    const el = panel?.querySelector<HTMLElement>(`[data-indice="${activo}"]`);
    if (!panel || !el) return;
    if (el.offsetTop < panel.scrollTop) panel.scrollTop = el.offsetTop - 4;
    else if (el.offsetTop + el.offsetHeight > panel.scrollTop + panel.clientHeight)
      panel.scrollTop = el.offsetTop + el.offsetHeight - panel.clientHeight + 4;
  }, [abierto, activo]);

  const onKeyDown = (e: React.KeyboardEvent<HTMLInputElement>) => {
    switch (e.key) {
      case 'ArrowDown':
        e.preventDefault();
        if (!abierto) abrir();
        else setResaltado(Math.min(activo + 1, visibles.length - 1));
        break;
      case 'ArrowUp':
        e.preventDefault();
        if (abierto) setResaltado(Math.max(activo - 1, 0));
        break;
      case 'Enter':
        e.preventDefault();
        if (!abierto) abrir();
        else if (visibles[activo]) elegir(visibles[activo]);
        break;
      case 'Escape':
        if (abierto) { e.preventDefault(); cerrar(); }
        break;
      case 'Tab':
        if (abierto) cerrar();
        break;
    }
  };

  return (
    <div ref={ref} style={{ position: 'relative', width: 'fit-content', ...restoStyle, minWidth: anchoMinimo }}>
      <div
        // Clic en el recuadro fuera del texto (p. ej. la flecha): abre o cierra sin perder el foco.
        onMouseDown={(e) => {
          if (disabled || e.target === inputRef.current) return;
          e.preventDefault();
          if (abierto) cerrar();
          else { inputRef.current?.focus(); abrir(); }
        }}
        style={estiloDisparador(abierto, background, disabled)}
      >
        <input
          ref={inputRef}
          type="text"
          role="combobox"
          aria-label={ariaLabel}
          aria-expanded={abierto}
          aria-controls={idLista}
          aria-autocomplete="list"
          aria-activedescendant={abierto && activo >= 0 ? idOpcion(activo) : undefined}
          autoComplete="off"
          spellCheck={false}
          disabled={disabled}
          value={abierto ? texto : seleccionado?.label ?? ''}
          placeholder={abierto ? seleccionado?.label ?? 'Escribe para buscar…' : ''}
          onMouseDown={() => abrir()}
          onChange={(e) => { setTexto(e.target.value); setResaltado(0); setAbierto(true); }}
          onKeyDown={onKeyDown}
          style={{
            // width 0 + flex 1: el ancho lo fija el contenedor (como el botón), no el input.
            flex: 1, width: 0, minWidth: 0, padding: 0, border: 'none', outline: 'none', background: 'transparent',
            fontSize: 13, color: 'var(--z-text-primary)',
            // Cerrado se ve como el botón normal (etiqueta en negrita); al escribir, texto normal.
            fontWeight: !abierto && seleccionado ? 600 : 400,
            textOverflow: 'ellipsis', cursor: disabled ? 'not-allowed' : abierto ? 'text' : 'pointer',
          }}
        />
        <span aria-hidden style={estiloFlecha(abierto)}>
          ▼
        </span>
      </div>

      {abierto && (
        // En pantallas angostas el panel no se sale de la vista (sin scroll horizontal).
        <div ref={panelRef} id={idLista} role="listbox" style={{ ...ESTILO_PANEL, maxWidth: 'min(420px, calc(100vw - 32px))' }}>
          {visibles.length === 0 && (
            <div style={{ padding: '9px 12px', fontSize: 12, color: 'var(--z-text-muted)' }}>
              Sin coincidencias para "{texto.trim()}".
            </div>
          )}
          {visibles.map((o, i) => {
            const sel = o.value === value;
            const destacada = sel || i === activo;
            return (
              <div
                key={i}
                id={idOpcion(i)}
                data-indice={i}
                role="option"
                aria-selected={sel}
                // mousedown + preventDefault: se elige sin que el input pierda el foco.
                onMouseDown={(e) => { e.preventDefault(); elegir(o); }}
                onMouseEnter={() => setResaltado(i)}
                style={estiloOpcion(destacada)}
              >
                <span style={estiloEtiquetaOpcion(destacada)}>{o.label}</span>
                {o.sublabel && <span style={ESTILO_SUBLABEL}>{o.sublabel}</span>}
              </div>
            );
          })}
        </div>
      )}
    </div>
  );
}
