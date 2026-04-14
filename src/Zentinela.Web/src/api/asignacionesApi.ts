import api from './authApi';
import type { PreviewAsignacionDto, ConfirmarCargueRequest, ResultadoCargueDto } from '../types/asignaciones';

export const generarPlantilla = async (): Promise<void> => {
  const response = await api.get('/api/asignaciones/plantilla', { responseType: 'blob' });
  const url  = URL.createObjectURL(response.data as Blob);
  const link = document.createElement('a');
  link.href     = url;
  link.download = 'plantilla_asignaciones.xlsx';
  link.click();
  URL.revokeObjectURL(url);
};

export const validarArchivo = async (
  archivo: File,
  carteraID: number,
  liderID: number,
): Promise<PreviewAsignacionDto> => {
  const formData = new FormData();
  formData.append('archivo',   archivo);
  formData.append('carteraID', String(carteraID));
  formData.append('liderID',   String(liderID));
  const response = await api.post<PreviewAsignacionDto>('/api/asignaciones/validar', formData, {
    headers: { 'Content-Type': 'multipart/form-data' },
  });
  return response.data;
};

export const confirmarCargue = async (
  request: ConfirmarCargueRequest,
): Promise<ResultadoCargueDto> => {
  const response = await api.post<ResultadoCargueDto>('/api/asignaciones/confirmar', request);
  return response.data;
};
