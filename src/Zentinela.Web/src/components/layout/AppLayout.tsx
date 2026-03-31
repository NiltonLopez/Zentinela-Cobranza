import { useState, useEffect } from 'react';
import { Outlet, useLocation } from 'react-router-dom';
import { Navbar } from './Navbar';
import { Sidebar } from './Sidebar';
import type { NavTab } from '../../types/ui';

function pathToTab(pathname: string): NavTab {
  if (pathname.startsWith('/carteras')) return 'carteras';
  if (pathname.startsWith('/kpis')) return 'reportes';
  return 'bases';
}

export function AppLayout() {
  const location = useLocation();
  const [activeTab, setActiveTab] = useState<NavTab>(pathToTab(location.pathname));

  useEffect(() => {
    setActiveTab(pathToTab(location.pathname));
  }, [location.pathname]);

  return (
    <div style={{ display: 'flex', flexDirection: 'column', height: '100vh', overflow: 'hidden' }}>
      <Navbar activeTab={activeTab} onTabChange={setActiveTab} />
      <div style={{ display: 'flex', flex: 1, overflow: 'hidden' }}>
        <Sidebar activeTab={activeTab} />
        <main style={{
          flex: 1,
          overflowY: 'auto',
          padding: '28px 32px',
          background: 'var(--z-bg-page)',
        }}>
          <Outlet />
        </main>
      </div>
    </div>
  );
}
