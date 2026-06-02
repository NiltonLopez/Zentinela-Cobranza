export function TarjetaResumenPagos({ label, valor }: { label: string; valor: string }) {
  return (
    <div style={{
      background: 'var(--z-bg-card)',
      border: '0.5px solid var(--z-border)',
      borderRadius: '8px',
      padding: '16px 20px',
      flex: '1 1 160px',
      minWidth: '160px',
    }}>
      <p style={{ fontSize: '11px', color: 'var(--z-text-muted)', marginBottom: '6px', textTransform: 'uppercase', letterSpacing: '0.04em' }}>
        {label}
      </p>
      <p style={{ fontSize: '20px', fontWeight: 600, color: 'var(--z-text-primary)', wordBreak: 'break-word' }}>
        {valor}
      </p>
    </div>
  );
}
