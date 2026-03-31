import type { CarteraDto } from '../types/carteras';

interface Props {
  cartera: CarteraDto;
  onClick: (cartera: CarteraDto) => void;
}

export function TarjetaCartera({ cartera, onClick }: Props) {
  return (
    <button
      onClick={() => onClick(cartera)}
      style={{
        width: '100%',
        textAlign: 'left',
        background: 'var(--z-bg-card)',
        border: '0.5px solid var(--z-border)',
        borderRadius: '8px',
        padding: '16px 20px',
        cursor: 'pointer',
        transition: 'border-color 0.15s, box-shadow 0.15s',
      }}
      onMouseOver={(e) => {
        const el = e.currentTarget as HTMLElement;
        el.style.borderColor = 'var(--z-brand)';
        el.style.boxShadow = '0 2px 8px rgba(245,73,39,0.08)';
      }}
      onMouseOut={(e) => {
        const el = e.currentTarget as HTMLElement;
        el.style.borderColor = 'var(--z-border)';
        el.style.boxShadow = 'none';
      }}
    >
      <p style={{ fontSize: '11px', fontWeight: 500, color: 'var(--z-text-muted)', textTransform: 'uppercase', letterSpacing: '0.04em', marginBottom: '6px' }}>
        Cartera
      </p>
      <div style={{ display: 'flex', justifyContent: 'space-between', alignItems: 'center' }}>
        <span style={{ fontSize: '14px', fontWeight: 500, color: 'var(--z-text-primary)' }}>
          {cartera.nombreCartera}
        </span>
        <span style={{ color: 'var(--z-border)', fontSize: '16px' }}>→</span>
      </div>
    </button>
  );
}
