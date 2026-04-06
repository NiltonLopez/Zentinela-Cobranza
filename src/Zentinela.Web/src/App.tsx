import { BrowserRouter, Routes, Route, Navigate } from 'react-router-dom';
import { QueryClient, QueryClientProvider } from '@tanstack/react-query';
import { AuthProvider } from './context/AuthContext';
import { RutaProtegida } from './components/RutaProtegida';
import { AppLayout } from './components/layout/AppLayout';

// Páginas públicas
import { LoginPage } from './pages/LoginPage';
import { RegisterPage } from './pages/RegisterPage';
import { CambiarPasswordPage } from './pages/CambiarPasswordPage';
import { SinPermisoPage } from './pages/SinPermisoPage';

// Home
import { HomeDashboardPage } from './pages/home/HomeDashboardPage';

// Cartera
import { CreadorZentinelaPage } from './pages/cartera/CreadorZentinelaPage';
import { CalificadorCarteraPage } from './pages/cartera/CalificadorCarteraPage';

// Localización
import { ArlSuraPage } from './pages/localizacion/ArlSuraPage';
import { CajacopiPage } from './pages/localizacion/CajacopiPage';
import { CamaraPage } from './pages/localizacion/CamaraPage';
import { FosygaPage } from './pages/localizacion/FosygaPage';
import { RuntPage } from './pages/localizacion/RuntPage';
import { SimitPage } from './pages/localizacion/SimitPage';

// Asignaciones
import { GestionAsignacionesPage } from './pages/asignaciones/GestionAsignacionesPage';
import { GestionCarterasPage } from './pages/asignaciones/GestionCarterasPage';
import { GestionPersonalPage } from './pages/asignaciones/GestionPersonalPage';

// Consulta
import { ConsultaIntegradaPage } from './pages/consulta/ConsultaIntegradaPage';

const queryClient = new QueryClient();

function App() {
  return (
    <QueryClientProvider client={queryClient}>
      <AuthProvider>
        <BrowserRouter>
          <Routes>
            {/* ── Rutas públicas ─────────────────────────────────────────── */}
            <Route path="/login" element={<LoginPage />} />
            <Route path="/registro" element={<RegisterPage />} />
            <Route path="/sin-permiso" element={<SinPermisoPage />} />

            {/* Cambio de contraseña — requiere auth, sin layout principal */}
            <Route
              path="/cambiar-password"
              element={<RutaProtegida><CambiarPasswordPage /></RutaProtegida>}
            />

            {/* ── Rutas protegidas bajo AppLayout ────────────────────────── */}
            <Route element={<RutaProtegida><AppLayout /></RutaProtegida>}>

              {/* Redirect raíz → /home */}
              <Route index element={<Navigate to="/home" replace />} />
              <Route path="/" element={<Navigate to="/home" replace />} />

              {/* Home — todos los roles autenticados */}
              <Route
                path="/home"
                element={
                  <RutaProtegida rolesPermitidos={['Admin', 'Líder', 'Director', 'Asesor']}>
                    <HomeDashboardPage />
                  </RutaProtegida>
                }
              />

              {/* Cartera */}
              <Route
                path="/cartera/creador"
                element={
                  <RutaProtegida rolesPermitidos={['Admin', 'Líder', 'Director']}>
                    <CreadorZentinelaPage />
                  </RutaProtegida>
                }
              />
              <Route
                path="/cartera/calificador"
                element={
                  <RutaProtegida rolesPermitidos={['Admin', 'Líder', 'Director']}>
                    <CalificadorCarteraPage />
                  </RutaProtegida>
                }
              />

              {/* Localización */}
              <Route
                path="/localizacion/arlsura"
                element={
                  <RutaProtegida rolesPermitidos={['Admin', 'Líder', 'Director']}>
                    <ArlSuraPage />
                  </RutaProtegida>
                }
              />
              <Route
                path="/localizacion/cajacopi"
                element={
                  <RutaProtegida rolesPermitidos={['Admin', 'Líder', 'Director']}>
                    <CajacopiPage />
                  </RutaProtegida>
                }
              />
              <Route
                path="/localizacion/camara"
                element={
                  <RutaProtegida rolesPermitidos={['Admin', 'Líder', 'Director']}>
                    <CamaraPage />
                  </RutaProtegida>
                }
              />
              <Route
                path="/localizacion/fosyga"
                element={
                  <RutaProtegida rolesPermitidos={['Admin', 'Líder', 'Director']}>
                    <FosygaPage />
                  </RutaProtegida>
                }
              />
              <Route
                path="/localizacion/runt"
                element={
                  <RutaProtegida rolesPermitidos={['Admin', 'Líder', 'Director']}>
                    <RuntPage />
                  </RutaProtegida>
                }
              />
              <Route
                path="/localizacion/simit"
                element={
                  <RutaProtegida rolesPermitidos={['Admin', 'Líder', 'Director']}>
                    <SimitPage />
                  </RutaProtegida>
                }
              />

              {/* Asignaciones — solo Admin */}
              <Route
                path="/asignaciones/gestion"
                element={
                  <RutaProtegida rolesPermitidos={['Admin']}>
                    <GestionAsignacionesPage />
                  </RutaProtegida>
                }
              />
              <Route
                path="/asignaciones/carteras"
                element={
                  <RutaProtegida rolesPermitidos={['Admin']}>
                    <GestionCarterasPage />
                  </RutaProtegida>
                }
              />
              <Route
                path="/asignaciones/personal"
                element={
                  <RutaProtegida rolesPermitidos={['Admin']}>
                    <GestionPersonalPage />
                  </RutaProtegida>
                }
              />

              {/* Consulta Integrada */}
              <Route
                path="/consulta"
                element={
                  <RutaProtegida rolesPermitidos={['Admin', 'Líder', 'Director', 'Asesor']}>
                    <ConsultaIntegradaPage />
                  </RutaProtegida>
                }
              />

            </Route>

            {/* Catch-all → /home */}
            <Route path="*" element={<Navigate to="/home" replace />} />
          </Routes>
        </BrowserRouter>
      </AuthProvider>
    </QueryClientProvider>
  );
}

export default App;
