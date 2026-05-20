import api from './authApi';
import type { RelacionLiderCartera, LiderDropdown, CarteraAutocomplete } from '../types/gestionCarteras';

export const listarRelaciones = async (): Promise<RelacionLiderCartera[]> => {
  const res = await api.get<RelacionLiderCartera[]>('/api/gestion-carteras/relaciones');
  return res.data;
};

export const listarLideres = async (): Promise<LiderDropdown[]> => {
  const res = await api.get<LiderDropdown[]>('/api/gestion-carteras/lideres');
  return res.data;
};

export const buscarCarteras = async (q: string): Promise<CarteraAutocomplete[]> => {
  const res = await api.get<CarteraAutocomplete[]>('/api/gestion-carteras/carteras/buscar', { params: { q } });
  return res.data;
};

export const crearRelacion = async (payload: {
  carteraID?: number;
  nombreCartera?: string;
  liderID: number;
  // Solo se respeta cuando la cartera es nueva (carteraID null). Si se selecciona
  // una cartera existente, el backend ignora el flag para no alterar su estado.
  esExterna?: boolean;
}): Promise<RelacionLiderCartera> => {
  const res = await api.post<RelacionLiderCartera>('/api/gestion-carteras/relaciones', payload);
  return res.data;
};

export const eliminarRelacion = async (liderID: number, carteraID: number): Promise<void> => {
  await api.delete(`/api/gestion-carteras/relaciones/${liderID}/${carteraID}`);
};
