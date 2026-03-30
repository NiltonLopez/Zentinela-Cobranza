import api from './authApi';
import type { CarteraDto } from '../types/carteras';

export const obtenerCarteras = async (): Promise<CarteraDto[]> => {
  const response = await api.get<CarteraDto[]>('/api/carteras');
  return response.data;
};
