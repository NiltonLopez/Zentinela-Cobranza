import api from './authApi';
import type { CalificadorCarteraDto, CarteraLiderDropdownDto } from '../types/calificador';

export const obtenerOpcionesCalificador = async (): Promise<CarteraLiderDropdownDto[]> => {
  const response = await api.get<CarteraLiderDropdownDto[]>('/api/calificador/opciones');
  return response.data;
};

export const calificarCartera = async (
  carteraID: number,
  liderID: number | null,
): Promise<CalificadorCarteraDto> => {
  // null → "Cartera Completo": URL sin segmento de líder, el backend recibe int? = null.
  const url = liderID == null
    ? `/api/calificador/${carteraID}`
    : `/api/calificador/${carteraID}/${liderID}`;
  const response = await api.get<CalificadorCarteraDto>(url);
  return response.data;
};
