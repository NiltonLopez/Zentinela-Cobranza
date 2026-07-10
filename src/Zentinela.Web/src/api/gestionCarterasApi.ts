import api from './authApi';
import type {
  RelacionLiderCartera, LiderDropdown, CarteraAutocomplete, EmpleadoElegible,
  CrearLiderExternoRequest, CrearLiderExternoResponse,
} from '../types/gestionCarteras';

export const listarRelaciones = async (): Promise<RelacionLiderCartera[]> => {
  const res = await api.get<RelacionLiderCartera[]>('/api/gestion-carteras/relaciones');
  return res.data;
};

export const listarLideres = async (): Promise<LiderDropdown[]> => {
  const res = await api.get<LiderDropdown[]>('/api/gestion-carteras/lideres');
  return res.data;
};

/** Empleados de RRHH (cargo de líder, activos, aún no líderes) para el alta de líder. */
export const listarEmpleadosElegibles = async (): Promise<EmpleadoElegible[]> => {
  const res = await api.get<EmpleadoElegible[]>('/api/gestion-carteras/empleados-elegibles');
  return res.data;
};

/** Crea un líder en DBCallCenter a partir de un empleado de RRHH (por cédula). */
export const crearLider = async (numeroDocumento: number): Promise<LiderDropdown> => {
  const res = await api.post<LiderDropdown>('/api/gestion-carteras/lideres', { numeroDocumento });
  return res.data;
};

/** Crea un líder externo (no empleado) + usuario Zentinela con rol Líder Externo. */
export const crearLiderExterno = async (
  payload: CrearLiderExternoRequest,
): Promise<CrearLiderExternoResponse> => {
  const res = await api.post<CrearLiderExternoResponse>('/api/gestion-carteras/lideres-externos', payload);
  return res.data;
};

export const buscarCarteras = async (q: string): Promise<CarteraAutocomplete[]> => {
  const res = await api.get<CarteraAutocomplete[]>('/api/gestion-carteras/carteras/buscar', { params: { q } });
  return res.data;
};

export const crearRelacion = async (payload: {
  carteraID?: number;
  nombreCartera?: string;
  liderID: number;
  // Solo se respeta cuando la cartera es nueva (carteraID null). Si se selecciona
  // una cartera existente, el backend ignora el flag para no alterar su estado.
  esExterna?: boolean;
}): Promise<RelacionLiderCartera> => {
  const res = await api.post<RelacionLiderCartera>('/api/gestion-carteras/relaciones', payload);
  return res.data;
};

export const eliminarRelacion = async (liderID: number, carteraID: number): Promise<void> => {
  await api.delete(`/api/gestion-carteras/relaciones/${liderID}/${carteraID}`);
};

// Soft-delete: retira la relación conservando su histórico (ADR-022).
export const inactivarRelacion = async (liderID: number, carteraID: number): Promise<void> => {
  await api.put(`/api/gestion-carteras/relaciones/${liderID}/${carteraID}/inactivar`);
};

export const reactivarRelacion = async (liderID: number, carteraID: number): Promise<void> => {
  await api.put(`/api/gestion-carteras/relaciones/${liderID}/${carteraID}/reactivar`);
};

export const descargarAsignacion = async (
  liderID: number,
  carteraID: number,
  nombreCartera: string,
  nombreLider: string,
): Promise<void> => {
  const res = await api.get(
    `/api/gestion-carteras/relaciones/${liderID}/${carteraID}/asignacion`,
    { responseType: 'blob' },
  );
  const url      = URL.createObjectURL(res.data as Blob);
  const cartera  = nombreCartera.replace(/\s+/g, '_');
  const lider    = nombreLider.replace(/\s+/g, '_');
  const link     = document.createElement('a');
  link.href      = url;
  link.download  = `asignacion_${cartera}_${lider}.xlsx`;
  link.click();
  URL.revokeObjectURL(url);
};
