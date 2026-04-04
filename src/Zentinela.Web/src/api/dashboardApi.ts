import api from './authApi';
import type { DashboardAsesorDto } from '../types/dashboard';

export const obtenerDashboardAsesor = async (): Promise<DashboardAsesorDto> => {
  const response = await api.get<DashboardAsesorDto>('/api/dashboard/asesor');
  return response.data;
};
