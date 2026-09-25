import { useState, type CSSProperties } from 'react';
import { useFotoPersona } from '../hooks/useFotoPersona';
import { TAMANOS_FOTO, type TamFoto } from '../api/personasApi';

// Fondos para las iniciales: tonos medios-oscuros, todos con contraste ≥ 4.5:1 contra texto
// blanco (WCAG AA). El color sale de un hash del nombre: la misma persona siempre tiene el mismo.
const FONDOS_INICIALES = [
  '#B91C1C', '#C2410C', '#A16207', '#15803D', '#0F766E',
  '#0369A1', '#1D4ED8', '#6D28D9', '#A21CAF', '#BE185D',
];

function colorDe(nombre: string): string {
  let h = 0;
  for (let i = 0; i < nombre.length; i++) h = (h * 31 + nombre.charCodeAt(i)) | 0;
  return FONDOS_INICIALES[Math.abs(h) % FONDOS_INICIALES.length];
}

/** "MARÍA JOSÉ PÉREZ" → "MJ"; "Carlos" → "C"; "" → "?". */
function inicialesDe(nombre: string): string {
  const palabras = nombre.trim().split(/\s+/).filter(Boolean);
  if (palabras.length === 0) return '?';
  return palabras.slice(0, 2).map(p => p[0]).join('').toLocaleUpperCase('es-CO');
}

/**
 * Miniatura que conviene pedir para un avatar de `tam` px en pantalla: la más chica de las que
 * genera el servidor que cubra tam × devicePixelRatio (nítida en pantallas 2x sin bajar de más).
 */
function tamParaPantalla(tam: number): TamFoto {
  const dpr = typeof window !== 'undefined' ? Math.min(window.devicePixelRatio || 1, 2) : 1;
  const necesario = Math.ceil(tam * dpr);
  return TAMANOS_FOTO.find(t => t >= necesario) ?? 320;
}

export interface AvatarPersonaProps {
  /** Cédula del líder o asesor. null/undefined → solo iniciales (no se consulta el API). */
  documento?: number | null;
  /** Nombre completo: alt de la foto, aria-label y fuente de las iniciales y del color. */
  nombre: string;
  /** Diámetro en px (default 32). */
  tam?: number;
  style?: CSSProperties;
}

/**
 * Avatar circular de una persona (ADR-040): la foto de RRHH si la hay; si no (o mientras carga,
 * o sin documento), sus iniciales sobre un color estable derivado del nombre.
 */
export function AvatarPersona({ documento, nombre, tam = 32, style }: AvatarPersonaProps) {
  const { url } = useFotoPersona(documento, tamParaPantalla(tam));
  // Si el <img> falla (blob corrupto), se vuelve a las iniciales para ESA url.
  const [urlFallida, setUrlFallida] = useState<string | null>(null);

  const base: CSSProperties = {
    width: tam,
    height: tam,
    borderRadius: '50%',
    flexShrink: 0,
    display: 'inline-block',
    overflow: 'hidden',
    verticalAlign: 'middle',
    ...style,
  };

  if (url && url !== urlFallida) {
    return (
      <img
        src={url}
        alt={nombre}
        title={nombre}
        width={tam}
        height={tam}
        onError={() => setUrlFallida(url)}
        style={{ ...base, objectFit: 'cover', background: 'var(--z-bg-page)' }}
      />
    );
  }

  return (
    <span
      role="img"
      aria-label={nombre}
      title={nombre}
      style={{
        ...base,
        display: 'inline-flex',
        alignItems: 'center',
        justifyContent: 'center',
        background: colorDe(nombre),
        color: '#FFF',
        fontSize: Math.max(10, Math.round(tam * 0.4)),
        fontWeight: 600,
        lineHeight: 1,
        letterSpacing: '0.02em',
        userSelect: 'none',
      }}
    >
      {inicialesDe(nombre)}
    </span>
  );
}
