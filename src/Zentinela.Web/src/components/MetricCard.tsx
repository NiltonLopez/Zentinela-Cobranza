interface Props {
  label: string;
  value: string | number;
  variant?: 'default' | 'brand';
}

export function MetricCard({ label, value, variant = 'default' }: Props) {
  return (
    <div style={{
      background: 'var(--z-bg-card)',
      border: '0.5px solid var(--z-border)',
      borderRadius: '8px',
      padding: '16px 20px',
      display: 'flex',
      flexDirection: 'column',
      gap: '6px',
      minWidth: 0,
    }}>
      <span style={{
        fontSize: '11px',
        fontWeight: 500,
        color: 'var(--z-text-muted)',
        textTransform: 'uppercase',
        letterSpacing: '0.04em',
      }}>
        {label}
      </span>
      <span style={{
        fontSize: '22px',
        fontWeight: 600,
        color: variant === 'brand' ? 'var(--z-brand)' : 'var(--z-text-primary)',
        letterSpacing: '-0.02em',
      }}>
        {typeof value === 'number' ? value.toLocaleString('es-CO') : value}
      </span>
    </div>
  );
}
