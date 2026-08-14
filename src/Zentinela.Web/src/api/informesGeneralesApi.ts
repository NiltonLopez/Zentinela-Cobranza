import api from './authApi';
import type { UsoBaseDatos } from '../types/informesGenerales';

/**
 * Sube N archivos CSV/XLSX con una columna de teléfono y devuelve el cruce contra
 * las fuentes de localización. Todos los archivos van en el mismo campo `archivos`
 * (el binding de ASP.NET los recibe como List<IFormFile>).
 */
export const analizarUsoBaseDatos = async (archivos: File[]): Promise<UsoBaseDatos> => {
  const form = new FormData();
  archivos.forEach((a) => form.append('archivos', a));
  const res = await api.post<UsoBaseDatos>('/api/informes/generales/uso-base-datos', form, {
    headers: { 'Content-Type': 'multipart/form-data' },
  });
  return res.data;
};
