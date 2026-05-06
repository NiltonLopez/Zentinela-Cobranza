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
import { PagosConfirmadosAsesorPage } from './pages/home/PagosConfirmadosAsesorPage';
import { PagosConfirmadosLiderPage } from './pages/home/PagosConfirmadosLiderPage';
import { PagosConfirmadosDirectorPage } from './pages/home/PagosConfirmadosDirectorPage';

// Cartera
import { CreadorZentinelaPage } from './pages/cartera/CreadorZentinelaPage';
import { CalificadorCarteraPage } from './pages/cartera/CalificadorCarteraPage';
import { ComparadorCarterasPage } from './pages/cartera/ComparadorCarterasPage';

// Informes
import { InformesVirtualidadPage } from './pages/informes/InformesVirtualidadPage';
import { InformesRecaudoPage } from './pages/informes/InformesRecaudoPage';

// Administración
import { GestionAsignacionesPage } from './pages/administracion/GestionAsignacionesPage';
import { GestionAgentesPage } from './pages/administracion/GestionAgentesPage';
import { CargueReportesPage } from './pages/administracion/CargueReportesPage';
import { GestionCarterasPage } from './pages/administracion/GestionCarterasPage';
import { GestionPersonalPage } from './pages/administracion/GestionPersonalPage';
import { CarguePagosPage } from './pages/administracion/CarguePagosPage';
import { AsignacionMetasPage } from './pages/administracion/AsignacionMetasPage';
import { CargueFuentesDatosPage } from './pages/administracion/CargueFuentesDatosPage';

// Consultas
import { ConsultaIntegradaPage } from './pages/consultas/ConsultaIntegradaPage';
import { RastreoLineasPage } from './pages/consultas/RastreoLineasPage';

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
              <Route
                path="/home/pagos"
                element={
                  <RutaProtegida rolesPermitidos={['Asesor']}>
                    <PagosConfirmadosAsesorPage />
                  </RutaProtegida>
                }
              />
              <Route
                path="/home/pagos-lider"
                element={
                  <RutaProtegida rolesPermitidos={['Líder']}>
                    <PagosConfirmadosLiderPage />
                  </RutaProtegida>
                }
              />
              <Route
                path="/home/pagos-director"
                element={
                  <RutaProtegida rolesPermitidos={['Director', 'Admin']}>
                    <PagosConfirmadosDirectorPage />
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

              <Route
                path="/cartera/comparador"
                element={
                  <RutaProtegida rolesPermitidos={['Admin', 'Líder', 'Director']}>
                    <ComparadorCarterasPage />
                  </RutaProtegida>
                }
              />

              {/* Informes */}
              <Route
                path="/informes/virtualidad"
                element={
                  <RutaProtegida rolesPermitidos={['Admin', 'Líder', 'Director']}>
                    <InformesVirtualidadPage />
                  </RutaProtegida>
                }
              />
              <Route
                path="/informes/recaudo"
                element={
                  <RutaProtegida rolesPermitidos={['Admin', 'Líder', 'Director']}>
                    <InformesRecaudoPage />
                  </RutaProtegida>
                }
              />

              {/* Administración — Admin + Auxiliar de Sistemas (excepto Metas) */}
              <Route
                path="/administracion/gestion"
                element={
                  <RutaProtegida rolesPermitidos={['Admin', 'Auxiliar de Sistemas']}>
                    <GestionAsignacionesPage />
                  </RutaProtegida>
                }
              />
              <Route
                path="/administracion/reportes"
                element={
                  <RutaProtegida rolesPermitidos={['Admin', 'Auxiliar de Sistemas']}>
                    <CargueReportesPage />
                  </RutaProtegida>
                }
              />
              <Route
                path="/administracion/carteras"
                element={
                  <RutaProtegida rolesPermitidos={['Admin', 'Auxiliar de Sistemas']}>
                    <GestionCarterasPage />
                  </RutaProtegida>
                }
              />
              <Route
                path="/administracion/personal"
                element={
                  <RutaProtegida rolesPermitidos={['Admin', 'Auxiliar de Sistemas']}>
                    <GestionPersonalPage />
                  </RutaProtegida>
                }
              />

              <Route
                path="/administracion/pagos"
                element={
                  <RutaProtegida rolesPermitidos={['Admin', 'Auxiliar de Sistemas']}>
                    <CarguePagosPage />
                  </RutaProtegida>
                }
              />

              <Route
                path="/administracion/metas"
                element={
                  <RutaProtegida rolesPermitidos={['Admin']}>
                    <AsignacionMetasPage />
                  </RutaProtegida>
                }
              />

              <Route
                path="/administracion/agentes"
                element={
                  <RutaProtegida rolesPermitidos={['Admin', 'Auxiliar de Sistemas']}>
                    <GestionAgentesPage />
                  </RutaProtegida>
                }
              />

              <Route
                path="/administracion/fuentes-datos"
                element={
                  <RutaProtegida rolesPermitidos={['Admin', 'Auxiliar de Sistemas']}>
                    <CargueFuentesDatosPage />
                  </RutaProtegida>
                }
              />

              {/* Consultas */}
              <Route
                path="/consultas/integrada"
                element={
                  <RutaProtegida rolesPermitidos={['Admin', 'Líder', 'Director', 'Asesor']}>
                    <ConsultaIntegradaPage />
                  </RutaProtegida>
                }
              />
              <Route
                path="/consultas/rastreo"
                element={
                  <RutaProtegida rolesPermitidos={['Admin', 'Líder', 'Director', 'Asesor']}>
                    <RastreoLineasPage />
                  </RutaProtegida>
                }
              />
              {/* Compatibilidad con la ruta antigua */}
              <Route path="/consulta" element={<Navigate to="/consultas/integrada" replace />} />

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
