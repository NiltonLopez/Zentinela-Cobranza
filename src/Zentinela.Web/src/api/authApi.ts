import axios from 'axios';
import type { LoginRequest, LoginResponse } from '../types/auth';
import { STORAGE_KEYS } from '../constants/storage';

// Si la SPA fue cargada por IP (acceso de respaldo cuando el DNS interno
// no resuelve `zentinela`), apuntamos a la API por la misma IP en su puerto
// dedicado. En cualquier otro caso usamos VITE_API_URL del entorno.
const PUERTO_API_FALLBACK = '5051';
const ES_IP = /^\d{1,3}\.\d{1,3}\.\d{1,3}\.\d{1,3}$/;

function resolverBaseURL(): string {
  if (typeof window !== 'undefined' && ES_IP.test(window.location.hostname)) {
    return `${window.location.protocol}//${window.location.hostname}:${PUERTO_API_FALLBACK}`;
  }
  return import.meta.env.VITE_API_URL;
}

const api = axios.create({
  baseURL: resolverBaseURL(),
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
