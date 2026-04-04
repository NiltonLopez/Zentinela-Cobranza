import { BrowserRouter, Routes, Route, Navigate } from 'react-router-dom';
import { QueryClient, QueryClientProvider } from '@tanstack/react-query';
import { AuthProvider } from './context/AuthContext';
import { RutaProtegida } from './components/RutaProtegida';
import { AppLayout } from './components/layout/AppLayout';
import { useAuth } from './context/AuthContext';
import { LoginPage } from './pages/LoginPage';
import { RegisterPage } from './pages/RegisterPage';
import { CambiarPasswordPage } from './pages/CambiarPasswordPage';
import { SinPermisoPage } from './pages/SinPermisoPage';
import { DashboardAsesorPage } from './pages/DashboardAsesorPage';
import { GeneradorPage } from './pages/GeneradorPage';
import { CarterasPage } from './pages/CarterasPage';
import { HistorialPage } from './pages/HistorialPage';
import { KPIsPage } from './pages/KPIsPage';

const queryClient = new QueryClient();

// Redirige al landing correcto según el rol del usuario autenticado
function RutaDefault() {
  const { usuario } = useAuth();
  if (usuario?.rol === 'Asesor') return <Navigate to="/dashboard" replace />;
  return <Navigate to="/bases" replace />;
}

function App() {
  return (
    <QueryClientProvider client={queryClient}>
      <AuthProvider>
        <BrowserRouter>
          <Routes>
            {/* Rutas públicas */}
            <Route path="/login" element={<LoginPage />} />
            <Route path="/registro" element={<RegisterPage />} />
            <Route path="/sin-permiso" element={<SinPermisoPage />} />

            {/* Cambio de contraseña — requiere auth pero sin layout */}
            <Route
              path="/cambiar-password"
              element={<RutaProtegida><CambiarPasswordPage /></RutaProtegida>}
            />

            {/* Rutas protegidas bajo AppLayout */}
            <Route element={<RutaProtegida><AppLayout /></RutaProtegida>}>
              <Route path="/dashboard" element={
                <RutaProtegida rolesPermitidos={['Asesor']}>
                  <DashboardAsesorPage />
                </RutaProtegida>
              } />
              <Route path="/bases" element={<GeneradorPage />} />
              <Route path="/carteras" element={<CarterasPage />} />
              <Route path="/historial" element={<HistorialPage />} />
              <Route path="/kpis" element={<KPIsPage />} />
            </Route>

            {/* Catch-all: redirige al landing según rol */}
            <Route path="*" element={<RutaProtegida><RutaDefault /></RutaProtegida>} />
          </Routes>
        </BrowserRouter>
      </AuthProvider>
    </QueryClientProvider>
  );
}

export default App;
