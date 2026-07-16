import api from './authApi';
import type {
  CanalVirtualidad,
  FilaSolicitud, ValidarSolicitudResponse,
  FilaResultado, ValidarResultadoResponse,
} from '../types/historicoVirtualidad';

const BASE = '/api/virtualidad/historico';

function descargarBlob(data: Blob, nombre: string): void {
  const url  = URL.createObjectURL(data);
  const link = document.createElement('a');
  link.href     = url;
  link.download = nombre;
  link.click();
  URL.revokeObjectURL(url);
}

// ── Fase Solicitud ──────────────────────────────────────────────────────────

export const generarPlantillaSolicitud = async (canal: CanalVirtualidad): Promise<void> => {
  const { data } = await api.get(`${BASE}/plantilla`, { params: { canal }, responseType: 'blob' });
  descargarBlob(data as Blob, `plantilla_solicitud_${canal.toLowerCase()}.xlsx`);
};

export const validarSolicitud = async (archivo: File, canal: CanalVirtualidad): Promise<ValidarSolicitudResponse> => {
  const fd = new FormData();
  fd.append('archivo', archivo);
  fd.append('canal', canal);
  const { data } = await api.post<ValidarSolicitudResponse>(`${BASE}/validar`, fd);
  return data;
};

export const confirmarSolicitud = async (
  canal: CanalVirtualidad, filasValidas: FilaSolicitud[],
): Promise<{ insertados: number; mensaje: string }> => {
  const { data } = await api.post(`${BASE}/confirmar`, { canal, filasValidas });
  return data;
};

// ── Fase Resultado ──────────────────────────────────────────────────────────

export const descargarPendientes = async (
  canal: CanalVirtualidad, carteraID?: number, desde?: string, hasta?: string,
): Promise<void> => {
  const { data } = await api.get(`${BASE}/pendientes`, {
    params: { canal, carteraID: carteraID || undefined, desde: desde || undefined, hasta: hasta || undefined },
    responseType: 'blob',
  });
  descargarBlob(data as Blob, `pendientes_${canal.toLowerCase()}.xlsx`);
};

export const validarResultado = async (archivo: File, canal: CanalVirtualidad): Promise<ValidarResultadoResponse> => {
  const fd = new FormData();
  fd.append('archivo', archivo);
  fd.append('canal', canal);
  const { data } = await api.post<ValidarResultadoResponse>(`${BASE}/resultado/validar`, fd);
  return data;
};

export const confirmarResultado = async (
  filasValidas: FilaResultado[],
): Promise<{ actualizados: number; mensaje: string }> => {
  const { data } = await api.post(`${BASE}/resultado/confirmar`, { filasValidas });
  return data;
};
