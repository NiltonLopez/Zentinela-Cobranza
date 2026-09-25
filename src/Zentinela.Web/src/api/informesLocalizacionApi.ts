import axios from 'axios';
import api from './authApi';
import type {
  AgruparCobertura,
  CoberturaLocalizacion,
  CriterioRanking,
  EstadoLocalizacion,
  FrescuraLocalizacion,
  RankingsLocalizacion,
  ResumenLocalizacion,
  UmbralFrescura,
} from '../types/informesLocalizacion';

// Informes de Localización (ADR-040). Todo lee el último corte del precálculo. El servidor fija el
// alcance del Líder (ignora el liderID de la URL): los filtros de aquí son para Admin/Director.

const BASE = '/api/informes/localizacion';

/** Alcance opcional: sin nada = toda la base (Admin/Director). */
export interface FiltroAlcance {
  liderID?: number;
  carteraID?: number;
}

const params = (f: FiltroAlcance) => ({
  ...(f.liderID ? { liderID: f.liderID } : {}),
  ...(f.carteraID ? { carteraID: f.carteraID } : {}),
});

export const obtenerEstadoLocalizacion = async (): Promise<EstadoLocalizacion> => {
  const { data } = await api.get<EstadoLocalizacion>(`${BASE}/resumen/estado`);
  return data;
};

export const obtenerResumenLocalizacion = async (f: FiltroAlcance): Promise<ResumenLocalizacion> => {
  const { data } = await api.get<ResumenLocalizacion>(`${BASE}/resumen`, { params: params(f) });
  return data;
};

export const obtenerCoberturaLocalizacion = async (
  f: FiltroAlcance,
  agrupar: AgruparCobertura,
): Promise<CoberturaLocalizacion> => {
  const { data } = await api.get<CoberturaLocalizacion>(`${BASE}/cobertura`, { params: { ...params(f), agrupar } });
  return data;
};

export const obtenerFrescuraLocalizacion = async (
  f: FiltroAlcance,
  umbral: UmbralFrescura,
): Promise<FrescuraLocalizacion> => {
  const { data } = await api.get<FrescuraLocalizacion>(`${BASE}/frescura`, { params: { ...params(f), umbral } });
  return data;
};

export const obtenerRankingsLocalizacion = async (
  criterio: CriterioRanking,
  incluirExternas: boolean,
): Promise<RankingsLocalizacion> => {
  const { data } = await api.get<RankingsLocalizacion>(`${BASE}/rankings`, { params: { criterio, incluirExternas } });
  return data;
};

/** Admin: pide un corte nuevo (202; corre en segundo plano). */
export const recalcularLocalizacion = async (): Promise<string> => {
  const { data } = await api.post<{ mensaje: string }>(`${BASE}/resumen/recalcular`);
  return data.mensaje;
};

/**
 * Traduce los errores propios del módulo: 503 = falta aplicar el script de las tablas; 409 = todavía no
 * hay corte (se está calculando el primero). null si es otro error.
 */
export function problemaLocalizacion(error: unknown): { tipo: 'sin-tablas' | 'sin-corte'; mensaje: string } | null {
  if (!axios.isAxiosError(error)) return null;
  const mensaje = (error.response?.data as { mensaje?: string } | undefined)?.mensaje ?? '';
  if (error.response?.status === 503) return { tipo: 'sin-tablas', mensaje };
  if (error.response?.status === 409) return { tipo: 'sin-corte', mensaje };
  return null;
}
