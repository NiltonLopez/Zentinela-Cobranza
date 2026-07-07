import { BrowserRouter, Routes, Route, Navigate } from 'react-router-dom';
import { QueryClient, QueryClientProvider } from '@tanstack/react-query';
import { AuthProvider, useAuth } from './context/AuthContext';
import { RutaProtegida } from './components/RutaProtegida';
import { AppLayout } from './components/layout/AppLayout';
import { rutaInicialPorRol } from './config/navigation';

// Redirige a la ruta inicial según el rol del usuario en sesión.
// Necesario para usuarios externos que no tienen acceso a /home.
function RedirigirAInicio() {
  const { usuario } = useAuth();
  return <Navigate to={rutaInicialPorRol(usuario?.rol)} replace />;
}

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
import { AgenciasExternasPage } from './pages/informes/AgenciasExternasPage';
import { AuditoriaGestionesPage } from './pages/informes/AuditoriaGestionesPage';

// Administración
import { GestionAsignacionesPage } from './pages/administracion/GestionAsignacionesPage';
import { GestionAgentesPage } from './pages/administracion/GestionAgentesPage';
import { CargueReportesPage } from './pages/administracion/CargueReportesPage';
import { GestionCarterasPage } from './pages/administracion/GestionCarterasPage';
import { GestionPersonalPage } from './pages/administracion/GestionPersonalPage';
import { CarguePagosPage } from './pages/administracion/CarguePagosPage';
import { AsignacionMetasPage } from './pages/administracion/AsignacionMetasPage';
import { CargueFuentesDatosPage } from './pages/administracion/CargueFuentesDatosPage';
import { GestionSolicitudesVirtualidadPage } from './pages/administracion/GestionSolicitudesVirtualidadPage';

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

            {/* TEMPORAL — preview sin auth de la página de Auditoría de Gestiones
                (datos mock, no requiere API/BD). REVERTIR antes de mergear:
                no debe quedar una ruta sin protección en producción. */}
            <Route
              path="/preview-auditoria"
              element={
                <div style={{ padding: '28px 32px', minHeight: '100vh', background: 'var(--z-bg-page)' }}>
                  <AuditoriaGestionesPage />
                </div>
              }
            />

            {/* Cambio de contraseña — requiere auth, sin layout principal */}
            <Route
              path="/cambiar-password"
              element={<RutaProtegida><CambiarPasswordPage /></RutaProtegida>}
            />

            {/* ── Rutas protegidas bajo AppLayout ────────────────────────── */}
            <Route element={<RutaProtegida><AppLayout /></RutaProtegida>}>

              {/* Redirect raíz → inicio según rol */}
              <Route index element={<RedirigirAInicio />} />
              <Route path="/" element={<RedirigirAInicio />} />

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
                  <RutaProtegida rolesPermitidos={['Admin', 'Líder', 'Director', 'Líder Externo']}>
                    <CreadorZentinelaPage />
                  </RutaProtegida>
                }
              />
              <Route
                path="/cartera/calificador"
                element={
                  <RutaProtegida rolesPermitidos={['Admin', 'Líder', 'Director', 'Líder Externo']}>
                    <CalificadorCarteraPage />
                  </RutaProtegida>
                }
              />

              <Route
                path="/cartera/comparador"
                element={
                  <RutaProtegida rolesPermitidos={['Admin', 'Líder', 'Director', 'Líder Externo']}>
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
              <Route
                path="/informes/agencias-externas"
                element={
                  <RutaProtegida rolesPermitidos={['Admin', 'Líder', 'Director']}>
                    <AgenciasExternasPage />
                  </RutaProtegida>
                }
              />
              <Route
                path="/informes/auditoria-gestiones"
                element={
                  <RutaProtegida rolesPermitidos={['Admin', 'Director']}>
                    <AuditoriaGestionesPage />
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

              <Route
                path="/administracion/solicitudes-virtualidad"
                element={
                  <RutaProtegida rolesPermitidos={['Admin', 'Auxiliar de Sistemas']}>
                    <GestionSolicitudesVirtualidadPage />
                  </RutaProtegida>
                }
              />

              {/* Consultas */}
              <Route
                path="/consultas/integrada"
                element={
                  <RutaProtegida rolesPermitidos={['Admin', 'Líder', 'Director', 'Asesor', 'Líder Externo', 'Asesor Externo']}>
                    <ConsultaIntegradaPage />
                  </RutaProtegida>
                }
              />
              <Route
                path="/consultas/rastreo"
                element={
                  <RutaProtegida rolesPermitidos={['Admin', 'Líder', 'Director', 'Asesor', 'Líder Externo']}>
                    <RastreoLineasPage />
                  </RutaProtegida>
                }
              />
              {/* Compatibilidad con la ruta antigua */}
              <Route path="/consulta" element={<Navigate to="/consultas/integrada" replace />} />

            </Route>

            {/* Catch-all → inicio según rol */}
            <Route path="*" element={<RedirigirAInicio />} />
          </Routes>
        </BrowserRouter>
      </AuthProvider>
    </QueryClientProvider>
  );
}

export default App;
