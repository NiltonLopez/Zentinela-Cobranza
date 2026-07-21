interface IconoProps {
  size?: number;
  color?: string;
}

/** Trofeo — usado cuando gana por % de cumplimiento de meta. */
export function IconoTrofeo({ size = 16, color = '#fff' }: IconoProps) {
  return (
    <svg width={size} height={size} viewBox="0 0 24 24" fill="none" aria-hidden="true" style={{ flexShrink: 0 }}>
      {/* Asas laterales */}
      <path d="M6 4H3.5V6.5C3.5 8.98528 5.51472 11 8 11" stroke={color} strokeWidth="1.6" strokeLinecap="round" opacity="0.9" />
      <path d="M18 4H20.5V6.5C20.5 8.98528 18.4853 11 16 11" stroke={color} strokeWidth="1.6" strokeLinecap="round" opacity="0.9" />
      {/* Copa */}
      <path d="M6 3.5H18V7.5C18 10.5376 15.5376 13 12.5 13H11.5C8.46243 13 6 10.5376 6 7.5V3.5Z" fill={color} />
      {/* Base y pie */}
      <rect x="10.7" y="13" width="2.6" height="3" fill={color} />
      <rect x="8" y="18.5" width="8" height="2" rx="1" fill={color} />
      <path d="M9 16H15L14.3 18.5H9.7L9 16Z" fill={color} opacity="0.9" />
    </svg>
  );
}

/** Bolsa de dinero — usada para el ganador por honorarios totales del mes. */
export function IconoBolsaDinero({ size = 16, color = '#fff' }: IconoProps) {
  return (
    <svg width={size} height={size} viewBox="0 0 24 24" fill="none" aria-hidden="true" style={{ flexShrink: 0 }}>
      {/* Nudo superior */}
      <path d="M9.5 3.5H14.5L13.3 6.5H10.7L9.5 3.5Z" fill={color} opacity="0.9" />
      {/* Cuerpo de la bolsa */}
      <path
        d="M10.7 6.5H13.3C16.5 6.5 19 9.8 19 13.5C19 17.6421 15.866 20.5 12 20.5C8.13401 20.5 5 17.6421 5 13.5C5 9.8 7.5 6.5 10.7 6.5Z"
        fill={color}
      />
      {/* Símbolo $ recortado */}
      <path
        d="M12 10.3V17.2M13.7 11.6C13.7 10.8 13 10.3 12 10.3C11 10.3 10.3 10.8 10.3 11.55C10.3 12.9 13.7 12.6 13.7 14.05C13.7 14.9 13 15.4 12 15.4C11 15.4 10.3 14.9 10.3 14.1"
        stroke="var(--z-brand)"
        strokeWidth="1.3"
        strokeLinecap="round"
        opacity="0.85"
      />
    </svg>
  );
}

/** Escala ascendente — usada para el ganador por productividad (honorarios/asesor). */
export function IconoEscalaSubiendo({ size = 16, color = '#fff' }: IconoProps) {
  return (
    <svg width={size} height={size} viewBox="0 0 24 24" fill="none" aria-hidden="true" style={{ flexShrink: 0 }}>
      {/* Barras ascendentes */}
      <rect x="4"  y="15" width="3.2" height="6"  rx="0.8" fill={color} opacity="0.75" />
      <rect x="10.4" y="10.5" width="3.2" height="10.5" rx="0.8" fill={color} opacity="0.9" />
      <rect x="16.8" y="6" width="3.2" height="15" rx="0.8" fill={color} />
      {/* Flecha ascendente */}
      <path d="M4 9.5L9.5 5L13.5 8L20 2.5" stroke={color} strokeWidth="1.7" strokeLinecap="round" strokeLinejoin="round" />
      <path d="M15.5 2.5H20V7" stroke={color} strokeWidth="1.7" strokeLinecap="round" strokeLinejoin="round" />
    </svg>
  );
}
