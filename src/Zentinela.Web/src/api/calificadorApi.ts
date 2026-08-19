import api from './authApi';
import type { CalificadorCarteraDto, CarteraLiderDropdownDto } from '../types/calificador';

export const obtenerOpcionesCalificador = async (): Promise<CarteraLiderDropdownDto[]> => {
  const response = await api.get<CarteraLiderDropdownDto[]>('/api/calificador/opciones');
  return response.data;
};

export const calificarCartera = async (
  carteraID: number,
  liderID: number | null,
  signal?: AbortSignal,
): Promise<CalificadorCarteraDto> => {
  // null → "Cartera Completo": URL sin segmento de líder, el backend recibe int? = null.
  const url = liderID == null
    ? `/api/calificador/${carteraID}`
    : `/api/calificador/${carteraID}/${liderID}`;
  const response = await api.get<CalificadorCarteraDto>(url, { signal });
  return response.data;
};

export type CategoriaLocalizacion = 'encontrado' | 'no-encontrado' | 'falta';

// Descarga CSV (streaming, BOM UTF-8 + separador ;) con (Cédula, Nombre,
// Última Consulta) de los titulares que clasifican en la celda indicada de la
// tabla "Porcentajes de Localización".
export const descargarLocalizacionCsv = async (
  carteraID: number,
  liderID: number | null,
  fuenteSlug: string,
  categoria: CategoriaLocalizacion,
): Promise<Blob> => {
  const params = liderID == null ? '' : `?liderID=${liderID}`;
  const res = await api.get(
    `/api/calificador/${carteraID}/localizacion/${fuenteSlug}/${categoria}${params}`,
    { responseType: 'blob' },
  );
  return res.data as Blob;
};

export interface CeldaLocalizacion {
  fuente: string;              // slug
  categoria: CategoriaLocalizacion;
}

// Descarga múltiple: ZIP con un CSV por celda seleccionada. POST porque la
// selección puede llegar a 60 celdas (20 fuentes × 3 categorías).
export const descargarLocalizacionZip = async (
  carteraID: number,
  liderID: number | null,
  celdas: CeldaLocalizacion[],
): Promise<Blob> => {
  const params = liderID == null ? '' : `?liderID=${liderID}`;
  const res = await api.post(
    `/api/calificador/${carteraID}/localizacion/zip${params}`,
    { celdas },
    { responseType: 'blob' },
  );
  return res.data as Blob;
};
