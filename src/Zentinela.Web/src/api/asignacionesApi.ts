import api from './authApi';
import type { PreviewAsignacionDto, ConfirmarCargueRequest, ResultadoCargueDto } from '../types/asignaciones';

export const generarPlantilla = async (completo = false): Promise<void> => {
  const url      = completo ? '/api/asignaciones/plantilla?completo=true' : '/api/asignaciones/plantilla';
  const response = await api.get(url, { responseType: 'blob' });
  const blobUrl  = URL.createObjectURL(response.data as Blob);
  const link     = document.createElement('a');
  link.href     = blobUrl;
  link.download = completo ? 'plantilla_asignaciones_completo.xlsx' : 'plantilla_asignaciones.xlsx';
  link.click();
  URL.revokeObjectURL(blobUrl);
};

export const validarArchivo = async (
  archivo: File,
  carteraID: number,
  // null → modo "Cartera Completo": el LiderID viene por fila en el Excel.
  liderID: number | null,
): Promise<PreviewAsignacionDto> => {
  const formData = new FormData();
  formData.append('archivo',   archivo);
  formData.append('carteraID', String(carteraID));
  if (liderID != null) formData.append('liderID', String(liderID));
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
