import api from './authApi';
import type {
  ValidarPagosResponse, CarteraDropdown, PagoFila,
  RegistrarPagoIndividualRequest, RegistrarPagoIndividualResponse,
  AsesorCartera, TitularEnCartera,
} from '../types/pagos';

export const generarPlantillaPagos = async (masivo = false): Promise<void> => {
  const url = masivo ? '/api/pagos/plantilla?masivo=true' : '/api/pagos/plantilla';
  const response = await api.get(url, { responseType: 'blob' });
  const blobUrl  = URL.createObjectURL(response.data as Blob);
  const link = document.createElement('a');
  link.href     = blobUrl;
  link.download = masivo ? 'plantilla_cargue_pagos_masivo.xlsx' : 'plantilla_cargue_pagos.xlsx';
  link.click();
  URL.revokeObjectURL(blobUrl);
};

export const listarCarterasPagos = async (): Promise<CarteraDropdown[]> => {
  const res = await api.get<CarteraDropdown[]>('/api/pagos/carteras');
  return res.data;
};

export const validarCsvPagos = async (
  archivo: File,
  carteraID: number | null,   // null en modo masivo (la cartera viene por fila)
  masivo = false,
): Promise<ValidarPagosResponse> => {
  const form = new FormData();
  form.append('archivo', archivo);
  if (carteraID != null) form.append('carteraID', String(carteraID));
  form.append('masivo', String(masivo));
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
  masivo = false,
): Promise<{ insertados: number; mensaje: string }> => {
  const res = await api.post<{ insertados: number; mensaje: string }>('/api/pagos/confirmar', {
    carteraID,
    masivo,
    filasValidas,
  });
  return res.data;
};

// ── Pago individual ────────────────────────────────────────────────────────────

export const registrarPagoIndividual = async (
  body: RegistrarPagoIndividualRequest,
): Promise<RegistrarPagoIndividualResponse> => {
  const res = await api.post<RegistrarPagoIndividualResponse>('/api/pagos/registrar-individual', body);
  return res.data;
};

export const buscarAsesoresCartera = async (
  carteraID: number,
  q: string,
): Promise<AsesorCartera[]> => {
  const res = await api.get<AsesorCartera[]>('/api/pagos/asesores', { params: { carteraID, q } });
  return res.data;
};

export const buscarTitularEnCartera = async (
  carteraID: number,
  cedula: number,
): Promise<TitularEnCartera> => {
  const res = await api.get<TitularEnCartera>('/api/pagos/titular', { params: { carteraID, cedula } });
  return res.data;
};
