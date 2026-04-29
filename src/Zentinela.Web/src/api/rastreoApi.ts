import api from './authApi';
import type { RastreoLinea } from '../types/rastreo';

export const rastrearLinea = async (numero: string): Promise<RastreoLinea> => {
  const res = await api.get<RastreoLinea>(`/api/consultas/rastreo/${numero}`);
  return res.data;
};
