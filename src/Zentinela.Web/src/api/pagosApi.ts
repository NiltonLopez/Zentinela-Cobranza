import api from './authApi';
import type { ValidarPagosResponse, CarteraDropdown, PagoFila } from '../types/pagos';

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
