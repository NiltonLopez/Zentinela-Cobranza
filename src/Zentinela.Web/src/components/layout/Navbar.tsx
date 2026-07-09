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
              style={{
                boxSizing: 'border-box',
                height: '100%',
                padding: '0 14px',
                background: 'none',
                border: 'none',
                borderBottomStyle: 'solid',
                borderBottomWidth: isActive ? '4px' : '2px',
                borderBottomColor: isActive ? 'var(--z-brand)' : 'transparent',
                color: isActive ? 'var(--z-brand)' : 'var(--z-text-muted)',
                fontSize: '13px',
                fontWeight: isActive ? 600 : 400,
                cursor: 'pointer',
                transition: 'color 0.15s, border-bottom-color 0.15s, border-bottom-width 0.15s',
                whiteSpace: 'nowrap',
              }}
              onMouseOver={(e) => {
                if (isActive) return;
                (e.currentTarget as HTMLElement).style.color = 'var(--z-brand)';
                (e.currentTarget as HTMLElement).style.borderBottomColor = 'var(--z-brand-light)';
              }}
              onMouseOut={(e) => {
                if (isActive) return;
                (e.currentTarget as HTMLElement).style.color = 'var(--z-text-muted)';
                (e.currentTarget as HTMLElement).style.borderBottomColor = 'transparent';
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
