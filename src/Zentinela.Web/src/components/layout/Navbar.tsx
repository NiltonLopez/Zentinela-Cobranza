import { useNavigate } from 'react-router-dom';
import { useAuth } from '../../context/AuthContext';
import { MODULOS, type ModuloKey } from '../../config/navigation';
import type { Rol } from '../../types/auth';
import ZentinelaLogo from '../ZentinelaLogo';

interface Props {
  moduloActivo: ModuloKey;
  onModuloChange: (modulo: ModuloKey) => void;
}

function getInitials(name: string): string {
  return name
    .split(' ')
    .filter(Boolean)
    .slice(0, 2)
    .map((n) => n[0].toUpperCase())
    .join('');
}

export function Navbar({ moduloActivo, onModuloChange }: Props) {
  const { usuario, cerrarSesion } = useAuth();
  const navigate = useNavigate();

  // Filtrar módulos visibles según el rol del usuario
  const modulosVisibles = MODULOS.filter((m) =>
    m.rolesPermitidos.includes(usuario?.rol as Rol)
  );

  const handleModuloClick = (key: ModuloKey) => {
    const modulo = MODULOS.find(m => m.key === key);
    const primero = (modulo?.subModulos ?? [])
      .filter(s => !s.rolesPermitidos || s.rolesPermitidos.includes(usuario?.rol as Rol))
      .slice()
      .sort((a, b) => a.label.localeCompare(b.label, 'es', { sensitivity: 'base' }))
      [0];
    onModuloChange(key);
    navigate(primero?.path ?? modulo?.defaultPath ?? '/');
  };

  const initials = usuario ? getInitials(usuario.nombreCompleto) : '?';

  return (
    <header style={{
      height: '52px',
      background: 'var(--z-bg-card)',
      borderBottom: '0.5px solid var(--z-border)',
      display: 'flex',
      alignItems: 'center',
      padding: '0 20px',
      flexShrink: 0,
      position: 'relative',
      zIndex: 10,
    }}>
      {/* Logo */}
      <div style={{ display: 'flex', alignItems: 'center', gap: '10px', minWidth: '160px' }}>
        <ZentinelaLogo size={28} />
        <span style={{ fontWeight: 600, fontSize: '15px', color: 'var(--z-text-primary)' }}>
          Zentinela
        </span>
      </div>

      {/* Módulos centrados */}
      <style>{`
        .z-navbar-tab {
          position: relative; box-sizing: border-box; transition: color 0.2s ease;
          color: var(--z-text-muted); font-weight: 400;
        }
        .z-navbar-tab::after {
          content: '';
          position: absolute;
          left: 50%; right: 50%; bottom: 0; height: 3px;
          background: var(--z-brand);
          transition: left 0.22s ease, right 0.22s ease;
        }
        .z-navbar-tab:hover { color: var(--z-brand); }
        .z-navbar-tab:hover::after { left: 10px; right: 10px; }
        .z-navbar-tab-activa,
        .z-navbar-tab-activa:hover { color: var(--z-brand); font-weight: 600; }
        .z-navbar-tab-activa::after,
        .z-navbar-tab-activa:hover::after { left: 0; right: 0; }
      `}</style>
      <nav style={{
        position: 'absolute',
        left: '50%',
        transform: 'translateX(-50%)',
        display: 'flex',
        alignItems: 'center',
        gap: '4px',
        height: '100%',
      }}>
        {modulosVisibles.map((modulo) => {
          const isActive = moduloActivo === modulo.key;
          return (
            <button
              key={modulo.key}
              onClick={() => handleModuloClick(modulo.key)}
              className={`z-navbar-tab${isActive ? ' z-navbar-tab-activa' : ''}`}
              style={{
                height: '100%',
                padding: '0 14px',
                background: 'none',
                border: 'none',
                fontSize: '13px',
                cursor: 'pointer',
                whiteSpace: 'nowrap',
              }}
            >
              {modulo.label}
            </button>
          );
        })}
      </nav>

      {/* Avatar y cerrar sesión */}
      <div style={{ marginLeft: 'auto', display: 'flex', alignItems: 'center', gap: '10px' }}>
        <button
          onClick={cerrarSesion}
          style={{
            background: 'none', border: 'none',
            fontSize: '12px', color: 'var(--z-text-muted)',
            cursor: 'pointer',
          }}
          onMouseOver={(e) => { (e.currentTarget as HTMLElement).style.color = '#e53e1a'; }}
          onMouseOut={(e) => { (e.currentTarget as HTMLElement).style.color = 'var(--z-text-muted)'; }}
        >
          Salir
        </button>
        <div
          title={usuario?.nombreCompleto ?? ''}
          style={{
            width: '32px', height: '32px',
            borderRadius: '50%',
            background: '#E2E2DF',
            display: 'flex', alignItems: 'center', justifyContent: 'center',
            fontSize: '11px', fontWeight: 600,
            color: 'var(--z-text-primary)',
            cursor: 'default',
            flexShrink: 0,
          }}
        >
          {initials}
        </div>
      </div>
    </header>
  );
}
