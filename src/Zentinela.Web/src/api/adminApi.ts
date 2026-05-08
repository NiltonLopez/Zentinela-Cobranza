import api from './authApi';
import type { UsuarioAdmin, Supervisor, AgenteAdmin, CarterasDelAgente } from '../types/admin';

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

export const listarSupervisoresActivos = async (): Promise<Supervisor[]> => {
  const response = await api.get<Supervisor[]>('/api/admin/supervisores-activos');
  return response.data;
};

export const obtenerSupervisorActual = async (usuarioId: number): Promise<Supervisor | null> => {
  const response = await api.get<Supervisor | null>(`/api/admin/usuarios/${usuarioId}/supervisor`);
  return response.data;
};

export const reasignarSupervisor = async (usuarioId: number, nuevoSupervisorId: number): Promise<void> => {
  await api.post(`/api/admin/usuarios/${usuarioId}/reasignar-supervisor`, { nuevoSupervisorId });
};

export const inactivarUsuario = async (id: number): Promise<void> => {
  await api.delete(`/api/admin/usuarios/${id}`);
};

export const cambiarUsuario = async (id: number, nuevoUsuario: string): Promise<void> => {
  await api.patch(`/api/admin/usuarios/${id}/usuario`, { nuevoUsuario });
};

export const listarAgentes = async (): Promise<AgenteAdmin[]> => {
  const response = await api.get<AgenteAdmin[]>('/api/admin/agentes');
  return response.data;
};

export const obtenerCarterasDelAgente = async (agenteID: number): Promise<CarterasDelAgente> => {
  const response = await api.get<CarterasDelAgente>(`/api/admin/agentes/${agenteID}/carteras`);
  return response.data;
};

export const asignarCarteraAgente = async (agenteID: number, carteraID: number): Promise<void> => {
  await api.post(`/api/admin/agentes/${agenteID}/carteras/${carteraID}`);
};

export const desasignarCarteraAgente = async (agenteID: number, carteraID: number): Promise<void> => {
  await api.delete(`/api/admin/agentes/${agenteID}/carteras/${carteraID}`);
};

export const cambiarRol = async (id: number, rol: string): Promise<void> => {
  await api.put(`/api/admin/usuarios/${id}/rol`, { rol });
};
