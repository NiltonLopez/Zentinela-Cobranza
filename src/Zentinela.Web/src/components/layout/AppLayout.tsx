import { useState, useEffect } from 'react';
import { Outlet, useLocation } from 'react-router-dom';
import { Navbar } from './Navbar';
import { Sidebar } from './Sidebar';
import { pathToModulo, type ModuloKey } from '../../config/navigation';

export function AppLayout() {
  const location = useLocation();
  const [moduloActivo, setModuloActivo] = useState<ModuloKey>(pathToModulo(location.pathname));

  useEffect(() => {
    setModuloActivo(pathToModulo(location.pathname));
  }, [location.pathname]);

  const esCartera = moduloActivo === 'cartera';
  const esConsultas = moduloActivo === 'consultas';
  const usaAlineacionFija = esCartera || esConsultas;

  return (
    <div style={{ display: 'flex', flexDirection: 'column', height: '100vh', overflow: 'hidden' }}>
      <Navbar moduloActivo={moduloActivo} onModuloChange={setModuloActivo} />
      <div style={{ display: 'flex', flex: 1, overflow: 'hidden', gap: usaAlineacionFija ? '24px' : undefined }}>
        <Sidebar moduloActivo={moduloActivo} />
        <main style={{
          flex: 1,
          overflowY: 'auto',
          background: 'var(--z-bg-page)',
        }}>
          <div
            className={usaAlineacionFija ? 'z-page-content z-page-content--align-fija' : 'z-page-content'}
            style={{
              maxWidth: '1440px',
              margin: '0 auto',
              padding: '28px 32px',
            }}
          >
            <Outlet />
          </div>
        </main>
      </div>
    </div>
  );
}
