import api from './authApi';
import type {
  ValidarMensajeriaResponse, InformeMensajeria, FilaMensajeria,
  ValidarIVRResponse, InformeIVR, FilaIVR,
  ValidarLlamadasResponse, InformeLlamadas, FilaLlamada,
  ValidarEntrantesResponse, InformeEntrantes, FilaEntrante,
  PagosImpacto,
} from '../types/virtualidad';

// ── Mensajería ────────────────────────────────────────────────────────────────

export const generarPlantillaMensajeria = async (): Promise<void> => {
  const { data } = await api.get('/api/virtualidad/mensajeria/plantilla', { responseType: 'blob' });
  const url  = URL.createObjectURL(data as Blob);
  const link = document.createElement('a');
  link.href     = url;
  link.download = 'plantilla_mensajeria.xlsx';
  link.click();
  URL.revokeObjectURL(url);
};

export const validarMensajeria = async (archivo: File, carteraID: number): Promise<ValidarMensajeriaResponse> => {
  const fd = new FormData();
  fd.append('archivo', archivo);
  fd.append('carteraID', String(carteraID));
  const { data } = await api.post<ValidarMensajeriaResponse>('/api/virtualidad/mensajeria/validar', fd);
  return data;
};

export const confirmarMensajeria = async (filasValidas: FilaMensajeria[]): Promise<{ insertados: number; mensaje: string }> => {
  const { data } = await api.post('/api/virtualidad/mensajeria/confirmar', { filasValidas });
  return data;
};

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

// ── IVR ───────────────────────────────────────────────────────────────────────

export const generarPlantillaIVR = async (): Promise<void> => {
  const { data } = await api.get('/api/virtualidad/ivr/plantilla', { responseType: 'blob' });
  const url  = URL.createObjectURL(data as Blob);
  const link = document.createElement('a');
  link.href     = url;
  link.download = 'plantilla_ivr.xlsx';
  link.click();
  URL.revokeObjectURL(url);
};

export const validarIVR = async (archivo: File, carteraID: number): Promise<ValidarIVRResponse> => {
  const fd = new FormData();
  fd.append('archivo', archivo);
  fd.append('carteraID', String(carteraID));
  const { data } = await api.post<ValidarIVRResponse>('/api/virtualidad/ivr/validar', fd);
  return data;
};

export const confirmarIVR = async (filasValidas: FilaIVR[]): Promise<{ insertados: number; mensaje: string }> => {
  const { data } = await api.post('/api/virtualidad/ivr/confirmar', { filasValidas });
  return data;
};

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
