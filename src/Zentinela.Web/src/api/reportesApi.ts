import api from './authApi';
import type { PreviewReporteDto, ConfirmarReporteRequest, ResultadoReporteDto } from '../types/reportes';

export const generarPlantillaReporte = async (): Promise<void> => {
  const response = await api.get('/api/reportes/plantilla', { responseType: 'blob' });
  const url  = URL.createObjectURL(response.data as Blob);
  const link = document.createElement('a');
  link.href     = url;
  link.download = 'plantilla_reporte_estados.xlsx';
  link.click();
  URL.revokeObjectURL(url);
};

export const validarArchivoReporte = async (
  archivo: File,
  carteraID: number,
): Promise<PreviewReporteDto> => {
  const formData = new FormData();
  formData.append('archivo', archivo);
  formData.append('carteraID', carteraID.toString());
  const response = await api.post<PreviewReporteDto>('/api/reportes/validar', formData, {
    headers: { 'Content-Type': 'multipart/form-data' },
  });
  return response.data;
};

export const confirmarCargueReporte = async (
  request: ConfirmarReporteRequest,
): Promise<ResultadoReporteDto> => {
  const response = await api.post<ResultadoReporteDto>('/api/reportes/confirmar', request);
  return response.data;
};

export const obtenerUltimaActualizacion = async (
  carteraID: number,
): Promise<{ fechaActualizacion: string | null }> => {
  const response = await api.get(`/api/reportes/ultima-actualizacion/${carteraID}`);
  return response.data;
};
