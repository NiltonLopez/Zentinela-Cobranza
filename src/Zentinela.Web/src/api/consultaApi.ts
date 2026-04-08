import type { ConsultaResult } from '../types/consulta';
import api from './authApi';

export const consultarPorCedula = async (cedula: string): Promise<ConsultaResult> => {
  const res = await api.get<ConsultaResult>(`/api/consulta/${cedula}`);
  return res.data;
};
