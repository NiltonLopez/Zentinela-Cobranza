import axios from 'axios';
import api from './authApi';
import type {
  AgruparCobertura,
  CoberturaLocalizacion,
  CriterioRanking,
  EstadoLocalizacion,
  FrescuraLocalizacion,
  ParametrosSugerencias,
  RankingsLocalizacion,
  ResumenLocalizacion,
  SugerenciasLocalizacion,
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

// ── Sugerencias (etapa 2) ─────────────────────────────────────────────────────
// En línea sobre el par: no dependen del precálculo. 409 = el ETL de gestión está corriendo; 400 = parámetros.

const paramsSugerencias = (p: ParametrosSugerencias) => ({
  carteraID: p.carteraID,
  ...(p.liderID ? { liderID: p.liderID } : {}),
  ...(p.fuentes.length ? { fuentes: p.fuentes.join(',') } : {}),
  umbralDias: p.umbralDias,
  ...(p.umbralRefrescoDias ? { umbralRefresco: p.umbralRefrescoDias } : {}),
  soloConSaldo: p.soloConSaldo,
  top: p.top,
  excluirEnviadosDias: p.excluirEnviadosDias,
  incluirLocalizados: p.incluirLocalizados,
});

export const obtenerSugerenciasLocalizacion = async (p: ParametrosSugerencias): Promise<SugerenciasLocalizacion> => {
  const { data } = await api.get<SugerenciasLocalizacion>(`${BASE}/sugerencias`, {
    params: paramsSugerencias(p),
    timeout: 320_000,
  });
  return data;
};

/**
 * Excel con el formato del Cargue de Fuentes: un .xlsx si hay una sola fuente con filas; un .zip con un .xlsx
 * por fuente si hay varias. Devuelve el blob y la extensión (según el Content-Type).
 */
export const descargarSugerenciasExcel = async (p: ParametrosSugerencias): Promise<{ blob: Blob; extension: 'xlsx' | 'zip' }> => {
  const res = await api.get(`${BASE}/sugerencias/excel`, {
    params: paramsSugerencias(p),
    responseType: 'blob',
    timeout: 320_000,
  });
  const tipo = String(res.headers['content-type'] ?? '');
  return { blob: res.data as Blob, extension: tipo.includes('zip') ? 'zip' : 'xlsx' };
};

/**
 * Mensaje de error de las sugerencias. Con responseType 'blob' el cuerpo del error llega como Blob: se lee
 * como texto. etl = el ETL de gestión está corriendo (409).
 */
export async function problemaSugerencias(error: unknown): Promise<{ etl: boolean; mensaje: string }> {
  if (!axios.isAxiosError(error)) return { etl: false, mensaje: 'No se pudieron calcular las sugerencias.' };
  let cuerpo: unknown = error.response?.data;
  if (cuerpo instanceof Blob) {
    try { cuerpo = JSON.parse(await cuerpo.text()); } catch { cuerpo = undefined; }
  }
  const c = cuerpo as { mensaje?: string; etlEnCurso?: boolean } | undefined;
  if (error.response?.status === 403) return { etl: false, mensaje: 'No tienes acceso a esa cartera.' };
  if (error.code === 'ECONNABORTED') return { etl: false, mensaje: 'La consulta tardó demasiado. Prueba con menos fuentes o un top menor.' };
  return {
    etl: error.response?.status === 409 && !!c?.etlEnCurso,
    mensaje: c?.mensaje || 'No se pudieron calcular las sugerencias.',
  };
}
