export function KPIsPage() {
  return (
    <div style={{ display: 'flex', flexDirection: 'column', gap: '20px', maxWidth: '900px' }}>
      <div>
        <h1 style={{ fontSize: '16px', fontWeight: 500, color: 'var(--z-text-primary)', marginBottom: '4px' }}>
          KPIs de cobranza
        </h1>
        <p style={{ fontSize: '13px', color: 'var(--z-text-muted)' }}>Métricas y reportes por líder</p>
      </div>
      <div style={{
        background: 'var(--z-bg-card)',
        border: '0.5px solid var(--z-border)',
        borderRadius: '8px',
        padding: '60px',
        textAlign: 'center',
      }}>
        <p style={{ fontSize: '13px', color: 'var(--z-text-muted)' }}>Próximamente</p>
      </div>
    </div>
  );
}
