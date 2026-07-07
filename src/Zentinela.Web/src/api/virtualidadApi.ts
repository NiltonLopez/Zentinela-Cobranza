import api from './authApi';
import type {
  InformeMensajeria,
  InformeIVR,
  ValidarLlamadasResponse, InformeLlamadas, FilaLlamada,
  ValidarEntrantesResponse, InformeEntrantes, FilaEntrante,
  PagosImpacto,
} from '../types/virtualidad';

// ── Mensajería (SMS) — solo informe; el alta se hace en Solicitudes de Virtualidad ──

export const getInformeMensajeria = async (
  desde: string,
  hasta: string,
  liderID?: number,
  carteraID?: number,
): Promise<InformeMensajeria> => {
  const { data } = await api.get<InformeMensajeria>('/api/virtualidad/mensajeria/informe', {
    params: { desde, hasta, liderID, carteraID },
  });
  return data;
};

// ── IVR — solo informe; el alta se hace en Solicitudes de Virtualidad ──────────

export const getInformeIVR = async (
  desde: string,
  hasta: string,
  liderID?: number,
  carteraID?: number,
): Promise<InformeIVR> => {
  const { data } = await api.get<InformeIVR>('/api/virtualidad/ivr/informe', {
    params: { desde, hasta, liderID, carteraID },
  });
  return data;
};

// ── Llamadas perdidas ─────────────────────────────────────────────────────────

export const validarLlamadasPerdidas = async (archivo: File): Promise<ValidarLlamadasResponse> => {
  const fd = new FormData();
  fd.append('archivo', archivo);
  const { data } = await api.post<ValidarLlamadasResponse>('/api/virtualidad/llamadas/perdidas/validar', fd);
  return data;
};

export const confirmarLlamadasPerdidas = async (filasValidas: FilaLlamada[]): Promise<{ insertados: number; mensaje: string }> => {
  const { data } = await api.post('/api/virtualidad/llamadas/perdidas/confirmar', { filasValidas });
  return data;
};

export const getInformeLlamadasPerdidas = async (): Promise<InformeLlamadas> => {
  const { data } = await api.get<InformeLlamadas>('/api/virtualidad/llamadas/perdidas/informe');
  return data;
};

// ── Llamadas entrantes ────────────────────────────────────────────────────────

export const validarLlamadasEntrantes = async (archivo: File): Promise<ValidarEntrantesResponse> => {
  const fd = new FormData();
  fd.append('archivo', archivo);
  const { data } = await api.post<ValidarEntrantesResponse>('/api/virtualidad/llamadas/entrantes/validar', fd);
  return data;
};

export const confirmarLlamadasEntrantes = async (filasParaGuardar: FilaEntrante[]): Promise<{ insertados: number; mensaje: string }> => {
  const { data } = await api.post('/api/virtualidad/llamadas/entrantes/confirmar', { filasParaGuardar });
  return data;
};

export const getInformeEntrantes = async (anio: number): Promise<InformeEntrantes> => {
  const { data } = await api.get<InformeEntrantes>('/api/virtualidad/llamadas/entrantes/informe', { params: { anio } });
  return data;
};

// ── Pagos vs Impacto ──────────────────────────────────────────────────────────

export const getInformePagosImpacto = async (desde: string, hasta: string, carteraID?: number): Promise<PagosImpacto> => {
  const { data } = await api.get<PagosImpacto>('/api/virtualidad/pagos-impacto/informe', { params: { desde, hasta, carteraID } });
  return data;
};
