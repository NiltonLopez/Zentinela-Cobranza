import api from './authApi';
import type {
  CanalVirtualidad,
  FilaSolicitud, ValidarSolicitudResponse,
  FilaResultado, ValidarResultadoResponse,
  CatalogoCanal, ValidarCargaIvrResponse, ConfirmarCargaIvrResponse,
} from '../types/historicoVirtualidad';

const BASE = '/api/virtualidad/historico';

// ── Catálogo ────────────────────────────────────────────────────────────────

export const obtenerCatalogoCanal = async (canal: CanalVirtualidad): Promise<CatalogoCanal> => {
  const { data } = await api.get<CatalogoCanal>(`${BASE}/catalogo`, { params: { canal } });
  return data;
};

// ── Carga única (canal IVR) ─────────────────────────────────────────────────

export const validarCargaIvr = async (
  archivo: File,
  onProgreso?: (pct: number) => void,
): Promise<ValidarCargaIvrResponse> => {
  const fd = new FormData();
  fd.append('archivo', archivo);
  const { data } = await api.post<ValidarCargaIvrResponse>(`${BASE}/ivr/validar`, fd, {
    onUploadProgress: (e) => {
      if (onProgreso && e.total) onProgreso(Math.round((100 * e.loaded) / e.total));
    },
  });
  return data;
};

/**
 * Reenvía el MISMO archivo que se validó, junto con su huella. El backend re-parsea
 * en vez de recibir las filas: evita un round-trip enorme con archivos grandes y
 * garantiza que lo insertado sea exactamente lo validado.
 */
export const confirmarCargaIvr = async (
  archivo: File,
  huella: string,
  onProgreso?: (pct: number) => void,
): Promise<ConfirmarCargaIvrResponse> => {
  const fd = new FormData();
  fd.append('archivo', archivo);
  fd.append('huella', huella);
  const { data } = await api.post<ConfirmarCargaIvrResponse>(`${BASE}/ivr/confirmar`, fd, {
    onUploadProgress: (e) => {
      if (onProgreso && e.total) onProgreso(Math.round((100 * e.loaded) / e.total));
    },
  });
  return data;
};

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
