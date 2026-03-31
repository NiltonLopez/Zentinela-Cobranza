import type { ButtonHTMLAttributes, ReactNode } from 'react';

interface Props extends ButtonHTMLAttributes<HTMLButtonElement> {
  variant?: 'primary' | 'ghost';
  children: ReactNode;
}

export function ZButton({ variant = 'primary', children, style, ...props }: Props) {
  const base: React.CSSProperties = {
    display: 'inline-flex',
    alignItems: 'center',
    gap: '6px',
    padding: '8px 16px',
    fontSize: '13px',
    fontWeight: 500,
    borderRadius: '6px',
    cursor: 'pointer',
    border: 'none',
    transition: 'opacity 0.15s, background 0.15s',
    lineHeight: 1,
  };

  const variants: Record<string, React.CSSProperties> = {
    primary: {
      background: 'var(--z-brand)',
      color: '#fff',
    },
    ghost: {
      background: 'transparent',
      border: '1px solid var(--z-border)',
      color: 'var(--z-text-muted)',
    },
  };

  return (
    <button
      style={{ ...base, ...variants[variant], ...style }}
      onMouseOver={(e) => { (e.currentTarget as HTMLButtonElement).style.opacity = '0.85'; }}
      onMouseOut={(e) => { (e.currentTarget as HTMLButtonElement).style.opacity = '1'; }}
      {...props}
    >
      {children}
    </button>
  );
}
