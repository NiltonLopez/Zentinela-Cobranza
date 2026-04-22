import { useNavigate, useLocation } from 'react-router-dom';
import { useAuth } from '../../context/AuthContext';
import { MODULOS, type ModuloKey } from '../../config/navigation';
import type { Rol } from '../../types/auth';

interface Props {
  moduloActivo: ModuloKey;
}

export function Sidebar({ moduloActivo }: Props) {
  const navigate = useNavigate();
  const location = useLocation();
  const { usuario } = useAuth();

  const modulo = MODULOS.find((m) => m.key === moduloActivo);
  const subModulos = (modulo?.subModulos ?? []).filter(
    (s) => !s.rolesPermitidos || s.rolesPermitidos.includes(usuario?.rol as Rol)
  );

  return (
    <aside style={{
      width: '180px',
      flexShrink: 0,
      background: 'var(--z-bg-card)',
      borderRight: '0.5px solid var(--z-border)',
      padding: '16px 8px',
      display: 'flex',
      flexDirection: 'column',
      gap: '2px',
      overflowY: 'auto',
    }}>
      {subModulos.map((sub) => {
        const isActive = location.pathname === sub.path;
        return (
          <button
            key={sub.key}
            onClick={() => navigate(sub.path)}
            style={{
              display: 'flex',
              alignItems: 'center',
              gap: '8px',
              padding: '8px 12px',
              borderRadius: '6px',
              background: isActive ? 'var(--z-brand-light)' : 'transparent',
              border: 'none',
              cursor: 'pointer',
              textAlign: 'left',
              width: '100%',
              transition: 'background 0.15s',
            }}
            onMouseOver={(e) => {
              if (!isActive) (e.currentTarget as HTMLElement).style.background = 'var(--z-bg-page)';
            }}
            onMouseOut={(e) => {
              if (!isActive) (e.currentTarget as HTMLElement).style.background = 'transparent';
            }}
          >
            {/* Punto indicador */}
            <span style={{
              width: '6px', height: '6px',
              borderRadius: '50%',
              background: isActive ? 'var(--z-brand)' : 'var(--z-border)',
              flexShrink: 0,
            }} />
            <span style={{
              fontSize: '13px',
              fontWeight: isActive ? 500 : 400,
              color: isActive ? 'var(--z-brand)' : 'var(--z-text-muted)',
            }}>
              {sub.label}
            </span>
          </button>
        );
      })}
    </aside>
  );
}
