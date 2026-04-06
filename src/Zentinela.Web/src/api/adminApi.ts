import api from './authApi';
import type { UsuarioAdmin } from '../types/admin';

export const listarUsuarios = async (): Promise<UsuarioAdmin[]> => {
  const response = await api.get<UsuarioAdmin[]>('/api/admin/usuarios');
  return response.data;
};

export const resetPassword = async (id: number): Promise<void> => {
  await api.post(`/api/admin/usuarios/${id}/reset-password`);
};
