export type CategoriaRanking = 'Diamante' | 'Oro' | 'Plata' | 'Bronce';

// Colores bien separados entre sí (evita que Oro y Bronce se confundan como con los emoji 🥇🥉).
const COLOR_CATEGORIA: Record<CategoriaRanking, { fill: string; stroke: string }> = {
  Diamante: { fill: '#BAE6FD', stroke: '#0C4A6E' },
  Oro:      { fill: '#FBBF24', stroke: '#78350F' },
  Plata:    { fill: '#CBD5E1', stroke: '#374151' },
  Bronce:   { fill: '#C87F45', stroke: '#5C2E0E' },
};

export function IconoCategoria({ categoria, size = 16 }: { categoria: CategoriaRanking; size?: number }) {
  const { fill, stroke } = COLOR_CATEGORIA[categoria];

  if (categoria === 'Diamante') {
    return (
      <svg width={size} height={size} viewBox="0 0 24 24" fill="none" aria-hidden="true" style={{ flexShrink: 0 }}>
        <path d="M4 8L8 3H16L20 8L12 21L4 8Z" fill={fill} stroke={stroke} strokeWidth="1.5" strokeLinejoin="round" />
        <path d="M4 8H20M8 3L9.5 8L12 21M16 3L14.5 8L12 21" stroke={stroke} strokeWidth="1" strokeLinejoin="round" strokeLinecap="round" opacity="0.55" />
      </svg>
    );
  }

  return (
    <svg width={size} height={size} viewBox="0 0 24 24" fill="none" aria-hidden="true" style={{ flexShrink: 0 }}>
      <path d="M8 12L5 21L12 18L19 21L16 12" fill={stroke} opacity="0.5" />
      <circle cx="12" cy="9" r="7" fill={fill} stroke={stroke} strokeWidth="1.5" />
      <path d="M12 5.5L13.2 7.9L15.8 8.3L13.9 10.1L14.4 12.7L12 11.4L9.6 12.7L10.1 10.1L8.2 8.3L10.8 7.9L12 5.5Z" fill={stroke} />
    </svg>
  );
}
