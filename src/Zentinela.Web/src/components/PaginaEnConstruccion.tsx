interface Props {
  titulo: string;
  descripcion?: string;
}

function IconoConstruccion() {
  return (
    <svg
      width="40"
      height="40"
      viewBox="0 0 24 24"
      fill="none"
      stroke="var(--z-brand)"
      strokeWidth="1.5"
      strokeLinecap="round"
      strokeLinejoin="round"
      aria-hidden="true"
    >
      {/* Llave inglesa */}
      <path d="M14.7 6.3a1 1 0 0 0 0 1.4l1.6 1.6a1 1 0 0 0 1.4 0l3.77-3.77a6 6 0 0 1-7.94 7.94l-6.91 6.91a2.12 2.12 0 0 1-3-3l6.91-6.91a6 6 0 0 1 7.94-7.94l-3.76 3.76z" />
    </svg>
  );
}

export function PaginaEnConstruccion({ titulo, descripcion }: Props) {
  return (
    <div style={{
      display: 'flex',
      alignItems: 'center',
      justifyContent: 'center',
      minHeight: '60vh',
    }}>
      <div style={{
        display: 'flex',
        flexDirection: 'column',
        alignItems: 'center',
        gap: '16px',
        maxWidth: '400px',
        textAlign: 'center',
      }}>
        {/* Icono con fondo suave */}
        <div style={{
          width: '72px',
          height: '72px',
          borderRadius: '16px',
          background: 'var(--z-brand-light)',
          border: '0.5px solid var(--z-border)',
          display: 'flex',
          alignItems: 'center',
          justifyContent: 'center',
          flexShrink: 0,
        }}>
          <IconoConstruccion />
        </div>

        {/* Título */}
        <h1 style={{
          fontSize: '16px',
          fontWeight: 600,
          color: 'var(--z-text-primary)',
          margin: 0,
        }}>
          {titulo}
        </h1>

        {/* Etiqueta "en desarrollo" */}
        <span style={{
          display: 'inline-block',
          padding: '3px 10px',
          borderRadius: '100px',
          fontSize: '11px',
          fontWeight: 600,
          letterSpacing: '0.04em',
          textTransform: 'uppercase',
          background: 'var(--z-brand-light)',
          color: 'var(--z-brand)',
          border: '0.5px solid #f5cbb8',
        }}>
          Módulo en desarrollo
        </span>

        {/* Descripción opcional */}
        {descripcion && (
          <p style={{
            fontSize: '13px',
            color: 'var(--z-text-muted)',
            lineHeight: '1.6',
            margin: 0,
          }}>
            {descripcion}
          </p>
        )}
      </div>
    </div>
  );
}
