import api from './authApi';

export interface RegistroRequest {
  cedula: number;
  usuarioControlNext: string;
}

export interface RegistroResponse {
  nombreCompleto: string;
  rol: string;
  mensaje: string;
}

export const registrarUsuario = async (data: RegistroRequest): Promise<RegistroResponse> => {
  const response = await api.post<RegistroResponse>('/api/registro', data);
  return response.data;
};
