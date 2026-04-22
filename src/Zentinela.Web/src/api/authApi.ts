import axios from 'axios';
import type { LoginRequest, LoginResponse } from '../types/auth';
import { STORAGE_KEYS } from '../constants/storage';

const api = axios.create({
  baseURL: import.meta.env.VITE_API_URL,
});

// Interceptor de request: agrega token JWT a todas las peticiones
api.interceptors.request.use((config) => {
  const token = localStorage.getItem(STORAGE_KEYS.TOKEN);
  if (token) {
    config.headers.Authorization = `Bearer ${token}`;
  }
  return config;
});

// Interceptor de respuesta: si el token expiró o es inválido, cierra la sesión.
// Excluye /api/auth/login para que las credenciales incorrectas no disparen el redirect.
api.interceptors.response.use(
  (response) => response,
  (error) => {
    const esLoginEndpoint = error.config?.url?.includes('/api/auth/login');
    if (error.response?.status === 401 && !esLoginEndpoint) {
      localStorage.removeItem(STORAGE_KEYS.TOKEN);
      localStorage.removeItem(STORAGE_KEYS.USUARIO);
      window.location.href = '/login';
    }
    return Promise.reject(error);
  }
);

export const login = async (data: LoginRequest): Promise<LoginResponse> => {
  const response = await api.post<LoginResponse>('/api/auth/login', data);
  return response.data;
};

export const cambiarPassword = async (data: {
  passwordActual: string;
  nuevoPassword: string;
}): Promise<void> => {
  await api.post('/api/auth/cambiar-password', data);
};

export default api;
