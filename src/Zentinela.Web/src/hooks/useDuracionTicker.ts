import { useLayoutEffect, useRef, useState } from 'react';

const VELOCIDAD_PX_POR_SEG = 40;
const DURACION_INICIAL_SEG = 36;

/**
 * Mide el ancho real de la pista (duplicada) de un ticker/marquee y calcula
 * la duración de animación necesaria para que todos los tickers de la app
 * viajen a la misma velocidad visual (px/seg), sin importar cuánto texto
 * traiga cada uno.
 *
 * Uso:
 *   const { ref, duracion } = useDuracionTicker(deps);
 *   <div ref={ref} style={{ animationDuration: duracion }}>...</div>
 */
export function useDuracionTicker(deps: unknown[]): { ref: React.RefObject<HTMLDivElement | null>; duracion: string } {
  const ref = useRef<HTMLDivElement>(null);
  const [duracionSeg, setDuracionSeg] = useState(DURACION_INICIAL_SEG);

  // eslint-disable-next-line react-hooks/exhaustive-deps
  useLayoutEffect(() => {
    const anchoPista = ref.current?.scrollWidth ?? 0;
    if (anchoPista > 0) {
      // La pista está duplicada (0% → -50%), así que la distancia recorrida
      // en un ciclo es la mitad del ancho total renderizado.
      setDuracionSeg((anchoPista / 2) / VELOCIDAD_PX_POR_SEG);
    }
  }, deps);

  return { ref, duracion: `${duracionSeg}s` };
}
