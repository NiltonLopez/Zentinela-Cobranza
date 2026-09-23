// Barra de progreso lineal. Mismo estilo que la barra de soporte del dashboard de
// director (DashboardDirectorPage, tarjeta de gestionados), un poco más alta porque
// aquí es el protagonista y no un adorno.
export function BarraProgreso({ porcentaje, etiqueta }: { porcentaje: number; etiqueta?: string }) {
  // El servidor puede reportar valores fuera de rango en los bordes de fase: se acota.
  const pct = Math.max(0, Math.min(100, Math.round(porcentaje || 0)));
  return (
    <div style={{ display: 'flex', flexDirection: 'column', gap: 6 }}>
      {etiqueta && (
        <div style={{ display: 'flex', justifyContent: 'space-between', alignItems: 'baseline', gap: 12 }}>
          <span style={{ fontSize: 13, fontWeight: 600, color: 'var(--z-text-primary)' }}>{etiqueta}</span>
          <span style={{ fontSize: 13, fontWeight: 600, color: 'var(--z-brand)', fontVariantNumeric: 'tabular-nums' }}>
            {pct}%
          </span>
        </div>
      )}
      <div
        role="progressbar" aria-valuenow={pct} aria-valuemin={0} aria-valuemax={100} aria-label={etiqueta}
        style={{ height: 6, borderRadius: 99, background: 'var(--z-border)', overflow: 'hidden' }}
      >
        <div style={{ height: '100%', width: `${pct}%`, background: 'var(--z-brand)', borderRadius: 99, transition: 'width 0.6s ease' }} />
      </div>
    </div>
  );
}
