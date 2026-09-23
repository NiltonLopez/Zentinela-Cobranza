// Tipos del sub-módulo "Cargue de Fuentes de Datos" (Administración).
// Los enums viajan como TEXTO (JsonStringEnumConverter del backend) y en camelCase.
import type { ErrorAgrupado } from './historicoVirtualidad';

export type { ErrorAgrupado };

/**
 * Clave de sessionStorage con el jobId del cargue que el usuario está siguiendo.
 * Permite salir de la página y volver (el job vive en el servidor). AuthContext la
 * limpia al cerrar sesión para que no la herede otro usuario en el mismo navegador.
 */
export const CLAVE_JOB_CARGUE = 'cargue-fuentes-job';

export type EstadoCargueJob =
  | 'Validando' | 'PreviewListo' | 'Cargando' | 'Completado' | 'Error' | 'Cancelado';

export type FaseCargueJob =
  | 'Leyendo' | 'Columnas' | 'Apoyo' | 'Limpieza' | 'Dedup' | 'Cruces'
  | 'Preview' | 'Escribiendo' | 'Auditoria' | 'Terminado';

// ── Catálogo de fuentes ─────────────────────────────────────────────────────

export interface ColumnaEsperada {
  nombre:      string;
  alias:       string[];
  tipo:        string;
  obligatoria: boolean;
  descripcion: string;
}

export interface FuenteCargue {
  fuente:               string;
  etiqueta:             string;
  tablaDestino:         string;
  notas:                string;
  habilitada:           boolean;
  faseIntegracion:      number;
  columnasEsperadas:    ColumnaEsperada[];
  extensionesAceptadas: string[];
  limiteFilasXlsx:      number;
}

// ── Preview (resultado de la validación) ────────────────────────────────────

export interface PreviewCargue {
  totalLeidas:           number;
  excluidas:             number;
  duplicadasEnArchivo:   number;
  candidatas:            number;
  insertadas:            number;
  refrescadas:           number;
  descartadasVacias:     number;
  vaciasQuePurgara:      number;
  titularesNuevos:       number;
  nombresReparados:      number;
  nombresIrrecuperables: number;
  relacionesNuevas:      number;
  filasSinCarteraLider:  number;
  columnasEncontradas:         string[];
  columnasFaltantes:           string[];
  columnasOpcionalesFaltantes: string[];
  errores:      ErrorAgrupado[];
  advertencias: ErrorAgrupado[];
  info:         ErrorAgrupado[];
  columnasMuestra: string[];
  muestra:         (string | null)[][];
  huellaArchivo: string;
  selloPrevisto: string;
}

// ── Resultado de la carga confirmada ────────────────────────────────────────

export interface ResultadoCargue {
  ejecucionID:        number;
  selloFechaConsulta: string;
  insertadas:         number;
  refrescadas:        number;
  descartadasVacias:  number;
  vaciasPurgadas:     number;
  titularesNuevos:    number;
  nombresReparados:   number;
  relacionesNuevas:   number;
  advertencias:       number;
  mensaje:            string;
  duracionSegundos:   number;
}

// ── Job en segundo plano ────────────────────────────────────────────────────

export interface CargueJob {
  jobId:          string;
  fuente:         string;
  etiqueta:       string;
  usuario:        string;
  nombreArchivo:  string;
  tamanoKB:       number;
  creadoEn:       string;
  actualizadoEn:  string;
  estado:         EstadoCargueJob;
  fase:           FaseCargueJob;
  porcentaje:     number;
  mensaje:        string | null;
  preview:        PreviewCargue | null;
  resultado:      ResultadoCargue | null;
  error:          string | null;
  puedeCancelar:  boolean;
  puedeConfirmar: boolean;
}

/**
 * Fila de "Cargues en curso / recientes": une los jobs en memoria del servidor con las
 * ejecuciones auditadas en BD (últimas 48 h). `estado` es texto libre porque puede venir
 * del job (EstadoCargueJob) o de la tabla de auditoría ('EN CURSO', 'OK', 'ERROR', …).
 */
export interface CargueJobResumen {
  jobId:         string | null;
  ejecucionID:   number | null;
  fuente:        string;
  usuario:       string;
  nombreArchivo: string;
  creadoEn:      string;
  estado:        string;
  porcentaje:    number;
  insertadas:    number | null;
  refrescadas:   number | null;
  enMemoria:     boolean;
}
