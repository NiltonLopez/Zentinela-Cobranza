import api from './authApi';
import type { UsuarioAdmin, Supervisor } from '../types/admin';

export const listarUsuarios = async (): Promise<UsuarioAdmin[]> => {
  const response = await api.get<UsuarioAdmin[]>('/api/admin/usuarios');
  return response.data;
};

export const resetPassword = async (id: number): Promise<void> => {
  await api.post(`/api/admin/usuarios/${id}/reset-password`);
};

export const listarSupervisores = async (): Promise<Supervisor[]> => {
  const response = await api.get<Supervisor[]>('/api/admin/supervisores');
  return response.data;
};

export const reasignarSupervisor = async (usuarioId: number, nuevoSupervisorId: number): Promise<void> => {
  await api.post(`/api/admin/usuarios/${usuarioId}/reasignar-supervisor`, { nuevoSupervisorId });
};

export const inactivarUsuario = async (id: number): Promise<void> => {
  await api.delete(`/api/admin/usuarios/${id}`);
};
