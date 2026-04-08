import api from './authApi';
import type { CalificadorCarteraDto, CarteraLiderDropdownDto } from '../types/calificador';

export const obtenerOpcionesCalificador = async (): Promise<CarteraLiderDropdownDto[]> => {
  const response = await api.get<CarteraLiderDropdownDto[]>('/api/calificador/opciones');
  return response.data;
};

export const calificarCartera = async (
  carteraID: number,
  liderID: number,
): Promise<CalificadorCarteraDto> => {
  const response = await api.get<CalificadorCarteraDto>(
    `/api/calificador/${carteraID}/${liderID}`,
  );
  return response.data;
};
