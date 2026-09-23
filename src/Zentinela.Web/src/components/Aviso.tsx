// Caja de aviso con acento lateral (info / warning / danger / success). Nació en el
// asistente SMS (creador-virtualidad/compartidosSms.tsx), que la re-exporta con su
// color morado; el resto de módulos usa el color de marca para 'info'.

type TipoAviso = 'info' | 'warning' | 'danger' | 'success';

const COLOR: Record<Exclude<TipoAviso, 'info'>, string> = {
  warning: '#d97706',
  danger:  '#dc2626',
  success: '#16a34a',
};
const FONDO: Record<Exclude<TipoAviso, 'info'>, string> = {
  warning: '#fffbeb',
  danger:  '#fef2f2',
  success: '#f0fdf4',
};

// Borde tenue a partir del color de acento. Con hex se usa el sufijo de alfa "30" (como
// siempre lo hizo el SMS); una variable CSS no admite sufijo, así que se mezcla con color-mix.
function bordeTenue(color: string): string {
  return color.startsWith('#') ? `${color}30` : `color-mix(in srgb, ${color} 19%, transparent)`;
}

export function Aviso({ tipo, children, colorInfo = 'var(--z-brand)', fondoInfo = 'var(--z-brand-light)' }: {
  tipo: TipoAviso;
  children: React.ReactNode;
  /** Color del acento para tipo 'info' (el SMS usa su morado). */
  colorInfo?: string;
  /** Fondo para tipo 'info'. */
  fondoInfo?: string;
}) {
  const color = tipo === 'info' ? colorInfo : COLOR[tipo];
  const bg    = tipo === 'info' ? fondoInfo : FONDO[tipo];
  return (
    <div style={{
      background: bg, border: `1px solid ${bordeTenue(color)}`, borderLeft: `4px solid ${color}`,
      borderRadius: 8, padding: '10px 14px', fontSize: 13, color: 'var(--z-text-primary)',
    }}>
      {children}
    </div>
  );
}
