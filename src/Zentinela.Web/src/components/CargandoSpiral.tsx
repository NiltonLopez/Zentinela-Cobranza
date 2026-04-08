interface Props {
  mensaje?: string;
}

export function CargandoSpiral({ mensaje = 'Cargando...' }: Props) {
  return (
    <div style={{
      display: 'flex', flexDirection: 'column', alignItems: 'center',
      justifyContent: 'center', padding: '44px 24px', gap: '16px',
    }}>
      <svg width="56" height="56" viewBox="0 0 56 56" fill="none" xmlns="http://www.w3.org/2000/svg">
        {/* Arco exterior — sentido horario, cola de cometa */}
        <circle
          cx="28" cy="28" r="24"
          stroke="#F54927"
          strokeWidth="4"
          strokeLinecap="round"
          strokeDasharray="100 51"
          style={{ animation: 'z-spiral-cw 1.1s linear infinite', transformOrigin: '28px 28px' }}
        />
        {/* Arco medio — antihorario, media opacidad */}
        <circle
          cx="28" cy="28" r="15"
          stroke="#F54927"
          strokeWidth="3"
          strokeOpacity="0.55"
          strokeLinecap="round"
          strokeDasharray="55 39"
          style={{ animation: 'z-spiral-ccw 0.75s linear infinite', transformOrigin: '28px 28px' }}
        />
        {/* Arco interior — horario, rápido */}
        <circle
          cx="28" cy="28" r="7"
          stroke="#F54927"
          strokeWidth="2.5"
          strokeOpacity="0.3"
          strokeLinecap="round"
          strokeDasharray="22 22"
          style={{ animation: 'z-spiral-cw 0.5s linear infinite', transformOrigin: '28px 28px' }}
        />
        {/* Punto central pulsante */}
        <circle
          cx="28" cy="28" r="3"
          fill="#F54927"
          style={{ animation: 'z-spiral-pulse 1.1s ease-in-out infinite', transformOrigin: '28px 28px' }}
        />
      </svg>

      {mensaje && (
        <p style={{
          fontSize: '12px',
          color: 'var(--z-text-muted)',
          letterSpacing: '0.04em',
        }}>
          {mensaje}
        </p>
      )}
    </div>
  );
}
