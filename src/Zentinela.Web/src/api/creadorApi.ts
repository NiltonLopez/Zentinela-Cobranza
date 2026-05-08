import api from './authApi';
import type {
  ColumnaBaseMadre,
  FiltroBaseMadreRequest,
  PreviewBaseMadreResponse,
  OpcionCarteraDto,
} from '../types/creador';

export const obtenerColumnas = async (): Promise<ColumnaBaseMadre[]> => {
  const res = await api.get<ColumnaBaseMadre[]>('/api/creador/columnas');
  return res.data;
};

export const obtenerOpcionesCreador = async (): Promise<OpcionCarteraDto[]> => {
  const res = await api.get<OpcionCarteraDto[]>('/api/creador/opciones');
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

// Streaming en backend — recomendado para carteras grandes (≥100k filas).
// Devuelve un Blob con el CSV completo (BOM UTF-8 + separador `;`).
export const exportarCsv = async (filtro: FiltroBaseMadreRequest): Promise<Blob> => {
  const res = await api.post('/api/creador/exportar-csv', filtro, { responseType: 'blob' });
  return res.data as Blob;
};
