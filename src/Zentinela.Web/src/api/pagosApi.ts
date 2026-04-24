import api from './authApi';
import type { ValidarPagosResponse, CarteraDropdown, PagoFila } from '../types/pagos';

export const generarPlantillaPagos = async (): Promise<void> => {
  const response = await api.get('/api/pagos/plantilla', { responseType: 'blob' });
  const url  = URL.createObjectURL(response.data as Blob);
  const link = document.createElement('a');
  link.href     = url;
  link.download = 'plantilla_cargue_pagos.xlsx';
  link.click();
  URL.revokeObjectURL(url);
};

export const listarCarterasPagos = async (): Promise<CarteraDropdown[]> => {
  const res = await api.get<CarteraDropdown[]>('/api/pagos/carteras');
  return res.data;
};

export const validarCsvPagos = async (
  archivo: File,
  carteraID: number,
): Promise<ValidarPagosResponse> => {
  const form = new FormData();
  form.append('archivo', archivo);
  form.append('carteraID', String(carteraID));
  const res = await api.post<ValidarPagosResponse>('/api/pagos/validar', form, {
    headers: { 'Content-Type': 'multipart/form-data' },
  });
  return res.data;
};

export const obtenerUltimaActualizacionPagos = async (
  carteraID: number,
): Promise<{ fechaActualizacion: string | null }> => {
  const res = await api.get(`/api/pagos/ultima-actualizacion/${carteraID}`);
  return res.data;
};

export const confirmarCarguePagos = async (
  carteraID: number,
  filasValidas: PagoFila[],
): Promise<{ insertados: number; mensaje: string }> => {
  const res = await api.post<{ insertados: number; mensaje: string }>('/api/pagos/confirmar', {
    carteraID,
    filasValidas,
  });
  return res.data;
};
