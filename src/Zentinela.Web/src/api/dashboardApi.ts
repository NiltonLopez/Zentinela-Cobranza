import api from './authApi';
import type {
  DashboardAsesorDto,
  DashboardDirectorDto,
  LiderDropdownDto,
  CarteraDropdownDto,
} from '../types/dashboard';

// ── Asesor ────────────────────────────────────────────────────────────────────

export const obtenerDashboardAsesor = async (): Promise<DashboardAsesorDto> => {
  const response = await api.get<DashboardAsesorDto>('/api/dashboard/asesor');
  return response.data;
};

// ── Director ──────────────────────────────────────────────────────────────────

export const obtenerLideresDropdown = async (): Promise<LiderDropdownDto[]> => {
  const response = await api.get<LiderDropdownDto[]>('/api/dashboard/director/lideres');
  return response.data;
};

export const obtenerCarterasLider = async (liderID: number): Promise<CarteraDropdownDto[]> => {
  const response = await api.get<CarteraDropdownDto[]>(`/api/dashboard/director/carteras/${liderID}`);
  return response.data;
};

export const obtenerDashboardDirector = async (
  liderID?: number | null,
  carteraID?: number | null,
): Promise<DashboardDirectorDto> => {
  const params: Record<string, string> = {};
  if (liderID != null)   params.liderID   = String(liderID);
  if (carteraID != null) params.carteraID = String(carteraID);
  const response = await api.get<DashboardDirectorDto>('/api/dashboard/director', { params });
  return response.data;
};
