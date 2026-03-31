import { useNavigate, useLocation } from 'react-router-dom';
import type { NavTab, SidebarItem } from '../../types/ui';

interface SidebarNavItem {
  key: SidebarItem;
  label: string;
  path: string;
}

const ITEMS_POR_TAB: Record<NavTab, SidebarNavItem[]> = {
  bases: [
    { key: 'generar-base', label: 'Generar base', path: '/bases' },
    { key: 'historial', label: 'Historial', path: '/historial' },
  ],
  carteras: [
    { key: 'mis-carteras', label: 'Mis carteras', path: '/carteras' },
  ],
  reportes: [
    { key: 'kpis', label: 'KPIs', path: '/kpis' },
  ],
};

interface Props {
  activeTab: NavTab;
}

export function Sidebar({ activeTab }: Props) {
  const navigate = useNavigate();
  const location = useLocation();
  const items = ITEMS_POR_TAB[activeTab] ?? [];

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
      {items.map((item) => {
        const isActive = location.pathname === item.path;
        return (
          <button
            key={item.key}
            onClick={() => navigate(item.path)}
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
              {item.label}
            </span>
          </button>
        );
      })}
    </aside>
  );
}
