import { useMemo, useState } from 'react';

export type SortDir = 'asc' | 'desc';
export interface SortState { key: string; dir: SortDir }

/**
 * Hook genérico de ordenamiento para tablas.
 *   - `items`     : datos a ordenar.
 *   - `initial`   : estado inicial (qué columna y dirección por defecto).
 *   - `getValue`  : extrae el valor comparable de cada item según la columna activa.
 *                   Retornar number → comparación numérica; string → localeCompare es-CO;
 *                   null/undefined → al final del orden.
 *
 * `toggle(key)`: si la columna ya está activa, alterna asc/desc; si no, activa la
 * columna con dirección `desc` (más útil por defecto para métricas grandes).
 */
export function useSort<T>(
  items: T[] | undefined,
  initial: SortState,
  getValue: (item: T, key: string) => string | number | null | undefined,
): { items: T[]; sort: SortState; toggle: (key: string) => void } {
  const [sort, setSort] = useState<SortState>(initial);

  const sorted = useMemo<T[]>(() => {
    if (!items || items.length === 0) return [];
    const arr = items.slice();
    arr.sort((a, b) => {
      const va = getValue(a, sort.key);
      const vb = getValue(b, sort.key);

      // Nullables al final, independiente de la dirección
      const aNil = va === null || va === undefined;
      const bNil = vb === null || vb === undefined;
      if (aNil && bNil) return 0;
      if (aNil) return 1;
      if (bNil) return -1;

      let cmp = 0;
      if (typeof va === 'number' && typeof vb === 'number') {
        cmp = va - vb;
      } else {
        cmp = String(va).localeCompare(String(vb), 'es', { sensitivity: 'base' });
      }
      return sort.dir === 'asc' ? cmp : -cmp;
    });
    return arr;
  }, [items, sort, getValue]);

  const toggle = (key: string) => {
    setSort(prev => prev.key === key
      ? { key, dir: prev.dir === 'asc' ? 'desc' : 'asc' }
      : { key, dir: 'desc' });
  };

  return { items: sorted, sort, toggle };
}
