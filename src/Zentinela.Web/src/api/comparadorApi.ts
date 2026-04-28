import api from './authApi';
import type { CarteraDropdownDto, ComparadorCarterasDto } from '../types/comparador';

export const obtenerOpcionesComparador = async (): Promise<CarteraDropdownDto[]> => {
  const res = await api.get<CarteraDropdownDto[]>('/api/comparador/opciones');
  return res.data;
};

export const compararCartera = async (carteraID: number): Promise<ComparadorCarterasDto> => {
  const res = await api.get<ComparadorCarterasDto>(`/api/comparador/${carteraID}`);
  return res.data;
};

const descargarBlob = (data: BlobPart, contentDisposition: string, fallback: string) => {
  const match = contentDisposition.match(/filename[^;=\n]*=((['"]).*?\2|[^;\n]*)/);
  const filename = match ? match[1].replace(/['"]/g, '') : fallback;
  const url = URL.createObjectURL(new Blob([data]));
  const a   = document.createElement('a');
  a.href     = url;
  a.download = filename;
  a.click();
  URL.revokeObjectURL(url);
};

const sanitizar = (nombre: string) =>
  nombre.replace(/[\\/:*?"<>|]/g, '').replace(/\s+/g, '_');

export const descargarExcelComparativo = async (
  carteraID: number,
  otraCarteraID: number,
  nombreSeleccionada: string,
  nombreOtra: string,
): Promise<void> => {
  const res = await api.get(`/api/comparador/${carteraID}/excel/${otraCarteraID}`, {
    responseType: 'blob',
  });
  descargarBlob(res.data, '', `Comparativo_${sanitizar(nombreSeleccionada)}_vs_${sanitizar(nombreOtra)}.xlsx`);
};

export const descargarExcelCompleto = async (
  carteraID: number,
  nombreSeleccionada: string,
): Promise<void> => {
  const res = await api.get(`/api/comparador/${carteraID}/excel`, {
    responseType: 'blob',
  });
  descargarBlob(res.data, '', `Comparativo_Completo_${sanitizar(nombreSeleccionada)}.xlsx`);
};
