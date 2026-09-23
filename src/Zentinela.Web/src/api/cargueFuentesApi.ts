import api from './authApi';
import type { CargueJob, CargueJobResumen, FuenteCargue } from '../types/cargueFuentes';

const BASE = '/api/cargue-fuentes';

type Progreso = (pct: number) => void;

// Mismo patrón que creadorVirtualidadApi: reporta el % de SUBIDA del archivo. El
// procesamiento corre después en el servidor y su % se consulta con obtenerJob.
function conProgreso(onProgreso?: Progreso) {
  return {
    onUploadProgress: (e: { loaded: number; total?: number }) => {
      if (onProgreso && e.total) onProgreso(Math.round((100 * e.loaded) / e.total));
    },
  };
}

export const obtenerFuentesCargue = async (): Promise<FuenteCargue[]> => {
  const { data } = await api.get<FuenteCargue[]>(`${BASE}/fuentes`);
  return data;
};

/**
 * Sube el archivo y arranca la validación en segundo plano. Responde 202 con el job
 * recién creado (estado 'Validando'); 409 si ya hay un cargue de esa fuente en curso.
 */
export const iniciarValidacion = async (
  fuente: string, archivo: File, onProgreso?: Progreso,
): Promise<CargueJob> => {
  const fd = new FormData();
  fd.append('archivo', archivo);
  const { data } = await api.post<CargueJob>(
    `${BASE}/${encodeURIComponent(fuente)}/validar`, fd, conProgreso(onProgreso),
  );
  return data;
};

/** 404 si el job ya no existe (se purgó o el servidor se reinició). */
export const obtenerJob = async (id: string): Promise<CargueJob> => {
  const { data } = await api.get<CargueJob>(`${BASE}/jobs/${id}`);
  return data;
};

export const listarJobs = async (): Promise<CargueJobResumen[]> => {
  const { data } = await api.get<CargueJobResumen[]>(`${BASE}/jobs`);
  return data;
};

export const confirmarJob = async (id: string): Promise<CargueJob> => {
  const { data } = await api.post<CargueJob>(`${BASE}/jobs/${id}/confirmar`);
  return data;
};

export const cancelarJob = async (id: string): Promise<CargueJob> => {
  const { data } = await api.post<CargueJob>(`${BASE}/jobs/${id}/cancelar`);
  return data;
};

export const descartarJob = async (id: string): Promise<void> => {
  await api.delete(`${BASE}/jobs/${id}`);
};
