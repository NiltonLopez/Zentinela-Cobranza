import { BrowserRouter, Routes, Route, Navigate } from 'react-router-dom';
import { QueryClient, QueryClientProvider } from '@tanstack/react-query';
import { AuthProvider } from './context/AuthContext';
import { RutaProtegida } from './components/RutaProtegida';
import { AppLayout } from './components/layout/AppLayout';
import { LoginPage } from './pages/LoginPage';
import { SinPermisoPage } from './pages/SinPermisoPage';
import { GeneradorPage } from './pages/GeneradorPage';
import { CarterasPage } from './pages/CarterasPage';
import { HistorialPage } from './pages/HistorialPage';
import { KPIsPage } from './pages/KPIsPage';

const queryClient = new QueryClient();

function App() {
  return (
    <QueryClientProvider client={queryClient}>
      <AuthProvider>
        <BrowserRouter>
          <Routes>
            {/* Rutas públicas */}
            <Route path="/login" element={<LoginPage />} />
            <Route path="/sin-permiso" element={<SinPermisoPage />} />

            {/* Rutas protegidas bajo AppLayout */}
            <Route element={<RutaProtegida><AppLayout /></RutaProtegida>}>
              <Route path="/bases" element={<GeneradorPage />} />
              <Route path="/carteras" element={<CarterasPage />} />
              <Route path="/historial" element={<HistorialPage />} />
              <Route path="/kpis" element={<KPIsPage />} />
            </Route>

            {/* Catch-all */}
            <Route path="*" element={<Navigate to="/bases" replace />} />
          </Routes>
        </BrowserRouter>
      </AuthProvider>
    </QueryClientProvider>
  );
}

export default App;
