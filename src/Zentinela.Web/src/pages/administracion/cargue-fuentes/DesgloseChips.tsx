// Chips de conteo desplegables del Cargue de Fuentes (preview y resultado). Cada chip se abre
// (clic o teclado) y muestra de qué se compone su número: grupos con título y cantidad, y
// cada grupo se abre a su vez con hasta 10 filas de ejemplo ("…y N más" si hay más). Pueden
// quedar varios abiertos; los paneles salen debajo de la fila de chips, en el orden de los
// chips. La composición la arma el servidor (DesgloseChip): aquí solo se pinta.
import { useState } from 'react';
import type { ChipCargue, DesgloseChip, GrupoDesglose, TonoDesglose } from '../../../types/cargueFuentes';
import { TONOS } from '../historico-virtualidad/compartidos';
import { num, type PaletaChip } from './formato';

export interface ChipDef {
  clave:   ChipCargue;
  numero:  number;
  label:   string;
  paleta:  PaletaChip;
  /** Unidad de la cantidad de cada grupo ([singular, plural]); null = solo el número. Por defecto, filas. */
  unidad?: [string, string] | null;
}

const TONO: Record<TonoDesglose, keyof typeof TONOS> = { Error: 'error', Aviso: 'aviso', Info: 'info' };

// Números con signo tipográfico: el grupo "agregadas por expansión" resta.
const cantidad = (n: number) => (n < 0 ? `−${num(-n)}` : num(n));

export function DesgloseChips({ chips, desglose, abiertosAlInicio = [] }: {
  chips: ChipDef[];
  desglose: DesgloseChip[] | null | undefined;
  /** Chips que arrancan abiertos (los que antes se mostraban siempre, como las excluidas). */
  abiertosAlInicio?: ChipCargue[];
}) {
  const [abiertos, setAbiertos] = useState<ReadonlySet<ChipCargue>>(() => new Set(abiertosAlInicio));

  const gruposDe = (clave: ChipCargue) => desglose?.find(d => d.clave === clave)?.grupos ?? [];
  // Un chip en 0 (o sin composición) no se despliega.
  const desplegable = (c: ChipDef) => c.numero !== 0 && gruposDe(c.clave).length > 0;
  const alternar = (clave: ChipCargue) => setAbiertos(prev => {
    const s = new Set(prev);
    if (s.has(clave)) s.delete(clave); else s.add(clave);
    return s;
  });

  const paneles = chips.filter(c => abiertos.has(c.clave) && desplegable(c));

  return (
    <div style={{ display: 'flex', flexDirection: 'column', gap: 12, minWidth: 0 }}>
      <div style={{ display: 'flex', gap: 12, flexWrap: 'wrap' }}>
        {chips.map(c => (
          <ChipDesplegable
            key={c.clave} chip={c} abierto={abiertos.has(c.clave)} desplegable={desplegable(c)}
            onAlternar={() => alternar(c.clave)}
          />
        ))}
      </div>
      {paneles.map(c => <PanelDesglose key={c.clave} chip={c} grupos={gruposDe(c.clave)} />)}
    </div>
  );
}

const idPanel = (clave: ChipCargue) => `desglose-cargue-${clave}`;

// Mismo aspecto que ChipConteo (compartidos.tsx), pero como <button>: foco, teclado y aria-expanded.
function ChipDesplegable({ chip, abierto, desplegable, onAlternar }: {
  chip: ChipDef; abierto: boolean; desplegable: boolean; onAlternar: () => void;
}) {
  const { bg, border, colorNum, colorLabel } = chip.paleta;
  const abiertoVisible = abierto && desplegable;
  return (
    <button
      type="button"
      onClick={desplegable ? onAlternar : undefined}
      disabled={!desplegable}
      aria-expanded={desplegable ? abiertoVisible : undefined}
      aria-controls={abiertoVisible ? idPanel(chip.clave) : undefined}
      title={desplegable ? (abiertoVisible ? 'Ocultar el detalle' : 'Ver de qué se compone') : undefined}
      style={{
        display: 'flex', alignItems: 'center', gap: 8, background: bg,
        border: `0.5px solid ${border}`, borderRadius: 8, padding: '12px 18px',
        cursor: desplegable ? 'pointer' : 'default', userSelect: 'none',
        boxShadow: abiertoVisible ? `inset 0 0 0 1px ${border}` : 'none',
        font: 'inherit', textAlign: 'left', color: 'inherit', maxWidth: '100%',
      }}
    >
      <span style={{ fontSize: 22, fontWeight: 700, color: colorNum }}>{num(chip.numero)}</span>
      <span style={{ fontSize: 13, color: colorLabel, minWidth: 0, overflowWrap: 'anywhere' }}>{chip.label}</span>
      {desplegable && (
        <span
          aria-hidden
          style={{
            fontSize: 11, color: colorLabel, marginLeft: 4, display: 'inline-block',
            transform: abiertoVisible ? 'rotate(180deg)' : 'none', transition: 'transform 0.15s ease',
          }}
        >
          ▾
        </span>
      )}
    </button>
  );
}

function PanelDesglose({ chip, grupos }: { chip: ChipDef; grupos: GrupoDesglose[] }) {
  const unidad = chip.unidad === undefined ? (['fila', 'filas'] as [string, string]) : chip.unidad;
  return (
    <section
      id={idPanel(chip.clave)}
      aria-label={`De qué se componen las ${num(chip.numero)} ${chip.label}`}
      style={{
        background: 'var(--z-bg-card)', border: `1px solid ${chip.paleta.border}`, borderRadius: 8,
        padding: '12px 16px', minWidth: 0,
      }}
    >
      <p style={{ margin: '0 0 8px', fontSize: 12, fontWeight: 600, color: chip.paleta.colorLabel }}>
        {num(chip.numero)} {chip.label} — de qué se compone
      </p>
      <ul style={{ margin: 0, padding: 0, listStyle: 'none', display: 'flex', flexDirection: 'column', gap: 6 }}>
        {grupos.map((g, i) => <FilaGrupo key={`${g.titulo}-${i}`} grupo={g} unidad={unidad} />)}
      </ul>
    </section>
  );
}

function FilaGrupo({ grupo, unidad }: { grupo: GrupoDesglose; unidad: [string, string] | null }) {
  const [abierto, setAbierto] = useState(false);
  const t = TONOS[TONO[grupo.tono] ?? 'info'];
  const desplegable = grupo.ejemplos.length > 0;
  const resto = Math.abs(grupo.cantidad) - grupo.ejemplos.length;
  const texto = (
    <span style={{ minWidth: 0, overflowWrap: 'anywhere' }}>
      {grupo.titulo}{' '}
      <strong>
        ({cantidad(grupo.cantidad)}{unidad ? ` ${Math.abs(grupo.cantidad) === 1 ? unidad[0] : unidad[1]}` : ''})
      </strong>
    </span>
  );
  const vineta: React.CSSProperties = { width: 12, flexShrink: 0, fontSize: 10, display: 'inline-block' };

  return (
    <li style={{ fontSize: 13, color: t.texto }}>
      {desplegable ? (
        <button
          type="button"
          onClick={() => setAbierto(a => !a)}
          aria-expanded={abierto}
          title={abierto ? 'Ocultar los ejemplos' : 'Ver filas de ejemplo'}
          style={{
            display: 'flex', alignItems: 'baseline', gap: 6, width: '100%', padding: 0, border: 'none',
            background: 'none', color: 'inherit', font: 'inherit', textAlign: 'left', cursor: 'pointer',
          }}
        >
          <span aria-hidden style={{ ...vineta, transform: abierto ? 'rotate(90deg)' : 'none', transition: 'transform 0.15s ease' }}>▶</span>
          {texto}
        </button>
      ) : (
        <div style={{ display: 'flex', alignItems: 'baseline', gap: 6 }}>
          <span aria-hidden style={vineta}>•</span>
          {texto}
        </div>
      )}
      {abierto && (
        <div style={{
          margin: '4px 0 2px 18px', padding: '6px 10px', background: t.bg,
          border: `0.5px solid ${t.borde}`, borderRadius: 6, minWidth: 0,
        }}>
          {grupo.ejemplos.map((ej, k) => (
            <div key={k} style={{ fontSize: 12, fontFamily: 'ui-monospace, Menlo, monospace', overflowWrap: 'anywhere' }}>{ej}</div>
          ))}
          {resto > 0 && (
            <div style={{ fontSize: 11, marginTop: 4, opacity: 0.8 }}>…y {num(resto)} más.</div>
          )}
        </div>
      )}
    </li>
  );
}
