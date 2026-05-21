import type { SortState } from '../hooks/useSort';

/**
 * Encabezado de tabla clickeable que dispara reorden de la columna.
 * Muestra ▲ / ▼ cuando esa columna está activa.
 *
 * Uso:
 *   <ThOrdenable label="Líder" sortKey="nombreLider" sort={sort} onSort={toggle} />
 *   <ThOrdenable label="Gestiones" sortKey="totalGestiones" sort={sort} onSort={toggle} align="right" />
 *
 * Si no se pasa `sortKey`, se renderiza como encabezado estático (no clickeable).
 */
export function ThOrdenable({
  label,
  sortKey,
  sort,
  onSort,
  align = 'left',
}: {
  label: string;
  sortKey?: string;
  sort?: SortState;
  onSort?: (key: string) => void;
  align?: 'left' | 'center' | 'right';
}) {
  const clickeable = !!sortKey && !!onSort;
  const activo     = clickeable && sort?.key === sortKey;
  const indicador  = activo ? (sort?.dir === 'asc' ? ' ▲' : ' ▼') : '';

  return (
    <th
      onClick={clickeable ? () => onSort!(sortKey!) : undefined}
      style={{
        padding: '10px 14px',
        textAlign: align,
        fontSize: '11px',
        fontWeight: 600,
        color: activo ? 'var(--z-text-primary)' : 'var(--z-text-muted)',
        borderBottom: '0.5px solid var(--z-border)',
        letterSpacing: '0.04em',
        textTransform: 'uppercase',
        whiteSpace: 'nowrap',
        cursor: clickeable ? 'pointer' : 'default',
        userSelect: 'none',
      }}
    >
      {label}{indicador}
    </th>
  );
}
