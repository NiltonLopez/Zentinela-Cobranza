import axios from 'axios';
import api from './authApi';
import type {
  AccionLote,
  AgruparCobertura,
  CoberturaLocalizacion,
  CrearEnvio,
  CriterioRanking,
  DetalleEnvioLocalizacion,
  EnvioCreado,
  EnvioLocalizacion,
  EstadoLocalizacion,
  FiltroEnvios,
  FrescuraLocalizacion,
  ListaEnviosLocalizacion,
  ParametrosSugerencias,
  RankingsLocalizacion,
  ResumenLocalizacion,
  SugerenciasLocalizacion,
  UmbralFrescura,
  UsoDepartamento,
  AlertasSemana,
  AsesoresLocalizacion,
  CriterioAsesor,
  TendenciaLocalizacion,
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

// ── Envíos a localización (etapa 3) ───────────────────────────────────────────
// 503 = falta el script de las tablas; 404 = el lote no existe o es de otro líder; 409 = otro usuario ya
// cambió el estado del lote (o, al crear, corre el ETL de gestión); 400 = parámetros.

/** Crea un lote BORRADOR: el servidor recalcula las sugerencias con estos parámetros y toma las filas de la fuente. */
export const crearEnvioLocalizacion = async (p: CrearEnvio): Promise<EnvioCreado> => {
  const { data } = await api.post<EnvioCreado>(`${BASE}/envios`, {
    ...p,
    liderID: p.liderID || null,
  }, { timeout: 320_000 });
  return data;
};

export const listarEnviosLocalizacion = async (f: FiltroEnvios): Promise<ListaEnviosLocalizacion> => {
  const { data } = await api.get<ListaEnviosLocalizacion>(`${BASE}/envios`, {
    params: {
      ...(f.liderID ? { liderID: f.liderID } : {}),
      ...(f.carteraID ? { carteraID: f.carteraID } : {}),
      ...(f.fuente ? { fuente: f.fuente } : {}),
      ...(f.estado ? { estado: f.estado } : {}),
    },
    timeout: 120_000,
  });
  return data;
};

export const obtenerEnvioLocalizacion = async (id: number, pagina: number, tam: number): Promise<DetalleEnvioLocalizacion> => {
  const { data } = await api.get<DetalleEnvioLocalizacion>(`${BASE}/envios/${id}`, { params: { pagina, tam }, timeout: 120_000 });
  return data;
};

export const cambiarEstadoEnvio = async (id: number, accion: AccionLote, motivo?: string): Promise<EnvioLocalizacion> => {
  const { data } = await api.post<EnvioLocalizacion>(`${BASE}/envios/${id}/${accion}`,
    accion === 'anular' ? { motivo: motivo?.trim() || null } : undefined);
  return data;
};

export const descargarEnvioExcel = async (id: number): Promise<Blob> => {
  const res = await api.get(`${BASE}/envios/${id}/excel`, { responseType: 'blob', timeout: 120_000 });
  return res.data as Blob;
};

export const obtenerUsoDepartamento = async (desde: string, hasta: string, liderID?: number): Promise<UsoDepartamento> => {
  const { data } = await api.get<UsoDepartamento>(`${BASE}/uso-departamento`, {
    params: { desde, hasta, ...(liderID ? { liderID } : {}) },
    timeout: 120_000,
  });
  return data;
};

/**
 * Mensaje de un error de envíos (con responseType 'blob' el cuerpo llega como Blob). sinTablas = 503 (falta el
 * script); conflicto = 409 (el lote cambió de estado, o corre el ETL al crear).
 */
export async function problemaEnvios(error: unknown): Promise<{ sinTablas: boolean; conflicto: boolean; mensaje: string }> {
  if (!axios.isAxiosError(error)) return { sinTablas: false, conflicto: false, mensaje: 'Ocurrió un error inesperado.' };
  let cuerpo: unknown = error.response?.data;
  if (cuerpo instanceof Blob) {
    try { cuerpo = JSON.parse(await cuerpo.text()); } catch { cuerpo = undefined; }
  }
  const mensaje = (cuerpo as { mensaje?: string } | undefined)?.mensaje;
  const status = error.response?.status;
  if (status === 403) return { sinTablas: false, conflicto: false, mensaje: 'No tienes acceso a ese par líder–cartera.' };
  if (error.code === 'ECONNABORTED') return { sinTablas: false, conflicto: false, mensaje: 'La consulta tardó demasiado. Vuelve a intentarlo.' };
  return {
    sinTablas: status === 503,
    conflicto: status === 409,
    mensaje: mensaje || (status === 404 ? 'No existe el lote o no tienes acceso a él.' : 'No se pudo completar la operación.'),
  };
}

// ── Etapa 4: tendencia, asesores y alertas de la semana ──────────────────────
// Mismo alcance que el Resumen (el servidor fija el liderID del Líder y valida su cartera).

/** Últimas `semanas` semanas (el servidor usa 12 si es 0; tope 26), un corte por semana. */
export const obtenerTendenciaLocalizacion = async (f: FiltroAlcance, semanas: number): Promise<TendenciaLocalizacion> => {
  const { data } = await api.get<TendenciaLocalizacion>(`${BASE}/tendencia`, { params: { ...params(f), semanas } });
  return data;
};

export const obtenerAsesoresLocalizacion = async (f: FiltroAlcance, criterio: CriterioAsesor): Promise<AsesoresLocalizacion> => {
  const { data } = await api.get<AsesoresLocalizacion>(`${BASE}/asesores`, { params: { ...params(f), criterio } });
  return data;
};

/** Se calculan al pedirlas; solo se muestran en la app. */
export const obtenerAlertasSemanaLocalizacion = async (f: FiltroAlcance): Promise<AlertasSemana> => {
  const { data } = await api.get<AlertasSemana>(`${BASE}/alertas-semana`, { params: params(f) });
  return data;
};
