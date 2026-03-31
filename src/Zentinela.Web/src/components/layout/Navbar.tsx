import { useNavigate } from 'react-router-dom';
import { useAuth } from '../../context/AuthContext';
import type { NavTab } from '../../types/ui';

interface Props {
  activeTab: NavTab;
  onTabChange: (tab: NavTab) => void;
}

function ShieldIcon() {
  return (
    <svg width="16" height="16" viewBox="0 0 24 24" fill="none" stroke="white" strokeWidth="2.5" strokeLinecap="round" strokeLinejoin="round">
      <path d="M12 22s8-4 8-10V5l-8-3-8 3v7c0 6 8 10 8 10z" />
    </svg>
  );
}

function getInitials(name: string): string {
  return name
    .split(' ')
    .filter(Boolean)
    .slice(0, 2)
    .map((n) => n[0].toUpperCase())
    .join('');
}

const TABS: { key: NavTab; label: string }[] = [
  { key: 'bases', label: 'Bases' },
  { key: 'carteras', label: 'Carteras' },
  { key: 'reportes', label: 'Reportes' },
];

// Tabs visibles por rol
const TABS_POR_ROL: Record<string, NavTab[]> = {
  Admin: ['bases', 'carteras', 'reportes'],
  Supervisor: ['bases', 'carteras', 'reportes'],
  Líder: ['bases', 'carteras'],
  Analista: ['bases', 'reportes'],
};

export function Navbar({ activeTab, onTabChange }: Props) {
  const { usuario, cerrarSesion } = useAuth();
  const navigate = useNavigate();

  const tabsVisibles = TABS.filter((t) =>
    TABS_POR_ROL[usuario?.rol ?? '']?.includes(t.key) ?? true
  );

  const handleTabChange = (tab: NavTab) => {
    onTabChange(tab);
    if (tab === 'bases') navigate('/bases');
    else if (tab === 'carteras') navigate('/carteras');
    else if (tab === 'reportes') navigate('/kpis');
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
      gap: '0',
      flexShrink: 0,
      position: 'relative',
      zIndex: 10,
    }}>
      {/* Logo */}
      <div style={{ display: 'flex', alignItems: 'center', gap: '10px', minWidth: '160px' }}>
        <div style={{
          width: '32px', height: '32px',
          background: 'var(--z-brand)',
          borderRadius: '8px',
          display: 'flex', alignItems: 'center', justifyContent: 'center',
          flexShrink: 0,
        }}>
          <ShieldIcon />
        </div>
        <span style={{ fontWeight: 600, fontSize: '15px', color: 'var(--z-text-primary)' }}>
          Zentinela
        </span>
      </div>

      {/* Tabs centradas */}
      <nav style={{
        position: 'absolute',
        left: '50%',
        transform: 'translateX(-50%)',
        display: 'flex',
        alignItems: 'center',
        gap: '4px',
        height: '100%',
      }}>
        {tabsVisibles.map((tab) => {
          const isActive = activeTab === tab.key;
          return (
            <button
              key={tab.key}
              onClick={() => handleTabChange(tab.key)}
              style={{
                height: '100%',
                padding: '0 14px',
                background: 'none',
                border: 'none',
                borderBottom: isActive ? '2px solid var(--z-brand)' : '2px solid transparent',
                color: isActive ? 'var(--z-brand)' : 'var(--z-text-muted)',
                fontSize: '13px',
                fontWeight: isActive ? 600 : 400,
                cursor: 'pointer',
                transition: 'color 0.15s',
              }}
              onMouseOver={(e) => {
                if (!isActive) (e.currentTarget as HTMLElement).style.color = 'var(--z-text-primary)';
              }}
              onMouseOut={(e) => {
                if (!isActive) (e.currentTarget as HTMLElement).style.color = 'var(--z-text-muted)';
              }}
            >
              {tab.label}
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
