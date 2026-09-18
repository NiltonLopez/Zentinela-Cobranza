import api from './authApi';
import type { MensajeAutorizado, GuardarMensajeAutorizado, ValidacionMensaje } from '../types/mensajesAutorizados';

const BASE = '/api/mensajes-autorizados';

export const listarMensajesAutorizados = async (soloActivos = false): Promise<MensajeAutorizado[]> => {
  const { data } = await api.get<MensajeAutorizado[]>(BASE, { params: { soloActivos } });
  return data;
};

export const validarMensajeAutorizado = async (req: GuardarMensajeAutorizado): Promise<ValidacionMensaje> => {
  const { data } = await api.post<ValidacionMensaje>(`${BASE}/validar`, req);
  return data;
};

export const crearMensajeAutorizado = async (req: GuardarMensajeAutorizado): Promise<MensajeAutorizado> => {
  const { data } = await api.post<MensajeAutorizado>(BASE, req);
  return data;
};

export const actualizarMensajeAutorizado = async (id: number, req: GuardarMensajeAutorizado): Promise<MensajeAutorizado> => {
  const { data } = await api.put<MensajeAutorizado>(`${BASE}/${id}`, req);
  return data;
};

export const cambiarActivoMensajeAutorizado = async (id: number, activo: boolean): Promise<void> => {
  await api.patch(`${BASE}/${id}/activo`, { activo });
};
