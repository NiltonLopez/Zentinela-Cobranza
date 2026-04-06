import api from './authApi';
import type { ColumnaBaseMadre, FiltroBaseMadreRequest, PreviewBaseMadreResponse } from '../types/creador';

export const obtenerColumnas = async (): Promise<ColumnaBaseMadre[]> => {
  const res = await api.get<ColumnaBaseMadre[]>('/api/creador/columnas');
  return res.data;
};

export const obtenerPreview = async (
  filtro: FiltroBaseMadreRequest,
): Promise<PreviewBaseMadreResponse> => {
  const res = await api.post<PreviewBaseMadreResponse>('/api/creador/preview', filtro);
  return res.data;
};

export const exportarExcel = async (filtro: FiltroBaseMadreRequest): Promise<Blob> => {
  const res = await api.post('/api/creador/exportar', filtro, { responseType: 'blob' });
  return res.data as Blob;
};
