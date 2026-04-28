import api from './authApi';
import type {
  DashboardAsesorDto,
  PagoAsesorDto,
  DashboardDirectorDto,
  LiderDropdownDto,
  CarteraDropdownDto,
  PagoDirectorDto,
} from '../types/dashboard';

// ── Asesor ────────────────────────────────────────────────────────────────────

export const obtenerDashboardAsesor = async (): Promise<DashboardAsesorDto> => {
  const response = await api.get<DashboardAsesorDto>('/api/dashboard/asesor');
  return response.data;
};

export const obtenerPagosAsesor = async (
  fechaDesde?: string,
  fechaHasta?: string,
): Promise<PagoAsesorDto[]> => {
  const params: Record<string, string> = {};
  if (fechaDesde) params.fechaDesde = fechaDesde;
  if (fechaHasta) params.fechaHasta = fechaHasta;
  const response = await api.get<PagoAsesorDto[]>('/api/dashboard/asesor/pagos', { params });
  return response.data;
};

// ── Líder ─────────────────────────────────────────────────────────────────────

export const obtenerCarterasLiderPropias = async (): Promise<CarteraDropdownDto[]> => {
  const response = await api.get<CarteraDropdownDto[]>('/api/dashboard/lider/carteras');
  return response.data;
};

export const obtenerDashboardLider = async (
  carteraID?: number | null,
  fechaDesde?: string,
  fechaHasta?: string,
): Promise<DashboardDirectorDto> => {
  const params: Record<string, string> = {};
  if (carteraID != null) params.carteraID  = String(carteraID);
  if (fechaDesde)        params.fechaDesde = fechaDesde;
  if (fechaHasta)        params.fechaHasta = fechaHasta;
  const response = await api.get<DashboardDirectorDto>('/api/dashboard/lider', { params });
  return response.data;
};

export const obtenerPagosLider = async (
  carteraID?: number | null,
  fechaDesde?: string,
  fechaHasta?: string,
): Promise<PagoDirectorDto[]> => {
  const params: Record<string, string> = {};
  if (carteraID != null) params.carteraID  = String(carteraID);
  if (fechaDesde)        params.fechaDesde = fechaDesde;
  if (fechaHasta)        params.fechaHasta = fechaHasta;
  const response = await api.get<PagoDirectorDto[]>('/api/dashboard/lider/pagos', { params });
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
  fechaDesde?: string,
  fechaHasta?: string,
): Promise<DashboardDirectorDto> => {
  const params: Record<string, string> = {};
  if (liderID != null)    params.liderID    = String(liderID);
  if (carteraID != null)  params.carteraID  = String(carteraID);
  if (fechaDesde)         params.fechaDesde = fechaDesde;
  if (fechaHasta)         params.fechaHasta = fechaHasta;
  const response = await api.get<DashboardDirectorDto>('/api/dashboard/director', { params });
  return response.data;
};

export const obtenerPagosDirector = async (
  liderID?: number | null,
  carteraID?: number | null,
  fechaDesde?: string,
  fechaHasta?: string,
): Promise<PagoDirectorDto[]> => {
  const params: Record<string, string> = {};
  if (liderID != null)    params.liderID    = String(liderID);
  if (carteraID != null)  params.carteraID  = String(carteraID);
  if (fechaDesde)         params.fechaDesde = fechaDesde;
  if (fechaHasta)         params.fechaHasta = fechaHasta;
  const response = await api.get<PagoDirectorDto[]>('/api/dashboard/director/pagos', { params });
  return response.data;
};
