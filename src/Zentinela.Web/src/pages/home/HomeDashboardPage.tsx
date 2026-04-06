import { useAuth } from '../../context/AuthContext';
import { DashboardAsesorPage } from './DashboardAsesorPage';
import { DashboardLiderPage } from './DashboardLiderPage';
import { DashboardDirectorPage } from './DashboardDirectorPage';

// Renderiza el dashboard correcto según el rol del usuario autenticado
export function HomeDashboardPage() {
  const { usuario } = useAuth();

  if (usuario?.rol === 'Asesor') return <DashboardAsesorPage />;
  if (usuario?.rol === 'Líder') return <DashboardLiderPage />;
  return <DashboardDirectorPage />;
}
