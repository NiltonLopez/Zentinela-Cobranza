import axios from 'axios';
import type { LoginRequest, LoginResponse } from '../types/auth';

const api = axios.create({
  baseURL: import.meta.env.VITE_API_URL,
});

// Interceptor: agrega token JWT a todas las peticiones
api.interceptors.request.use((config) => {
  const token = localStorage.getItem('zentinela_token');
  if (token) {
    config.headers.Authorization = `Bearer ${token}`;
  }
  return config;
});

export const login = async (data: LoginRequest): Promise<LoginResponse> => {
  const response = await api.post<LoginResponse>('/api/auth/login', data);
  return response.data;
};

export default api;
