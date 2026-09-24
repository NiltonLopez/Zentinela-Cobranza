// Tipos del módulo "Gestión de Solicitudes de Virtualidad" (IVR / SMS / Correo).

export type CanalVirtualidad = 'IVR' | 'SMS' | 'Correo';

// ── Fase Solicitud ──────────────────────────────────────────────────────────

export interface FilaSolicitud {
  numeroDocumento: number;
  destino:         string;
  nombreCartera:   string;
  nombreLider:     string;
  carteraID:       number;
  liderID:         number;
  fechaEnvio:      string;
  esValida:        boolean;
  errorMensaje:    string | null;
}

export interface AlertaSolicitud {
  destino:       string;
  nombreCartera: string;
  totalEnvios:   number;
}

// Un tipo de error con su conteo de filas — no se limita a la muestra de `preview`.
export interface ErrorAgrupado {
  mensaje:  string;
  cantidad: number;
  /** Clave del motivo, para emparejar un contador con su grupo. Ausente fuera del Creador de Virtualidad. */
  motivo?:  string | null;
  /** Hasta diez filas concretas del grupo, para desplegar el detalle. */
  ejemplos?: string[];
}

export interface ValidarSolicitudResponse {
  totalFilas:       number;
  totalValidas:     number;
  totalErrores:     number;
  preview:          FilaSolicitud[];
  filasValidas:     FilaSolicitud[];
  alertas:          AlertaSolicitud[];
  erroresAgrupados: ErrorAgrupado[];
}

// ── Fase Resultado ──────────────────────────────────────────────────────────

export interface FilaResultado {
  id:           number;
  estado:       string;
  esValida:     boolean;
  errorMensaje: string | null;
}

export interface ValidarResultadoResponse {
  totalFilas:       number;
  totalValidas:     number;
  totalErrores:     number;
  preview:          FilaResultado[];
  filasValidas:     FilaResultado[];
  erroresAgrupados: ErrorAgrupado[];
}

// ── Catálogo por canal (fuente única: la sirve el backend) ──────────────────

/** Debe coincidir con el enum ModoCargaCanal del backend (JsonStringEnumConverter). */
export type ModoCargaCanal = 'DosFases' | 'ArchivoUnico';

export interface MapeoEstado {
  codigoCrudo: string;
  estado:      string;
  significado: string;
}

export interface CatalogoCanal {
  canal:                CanalVirtualidad;
  modo:                 ModoCargaCanal;
  estados:              string[];
  mapeoEstados:         MapeoEstado[];
  columnasEsperadas:    string[];
  extensionesAceptadas: string[];
}

// ── Carga única (canal IVR) ─────────────────────────────────────────────────

export interface FilaIvr {
  numeroDocumento: number;
  destino:         string;
  nombreCartera:   string;
  nombreLider:     string;
  carteraID:       number;
  liderID:         number;
  fechaEnvio:      string;
  codigoCrudo:     string;
  estado:          string | null;
  esValida:        boolean;
  errorMensaje:    string | null;
}

export interface ConteoEstado {
  estado:      string;
  codigoCrudo: string;
  cantidad:    number;
  porcentaje:  number;
}

export interface ConteoCartera {
  nombreCartera: string;
  nombreLider:   string;
  cantidad:      number;
}

export interface Solapamiento {
  carteraID:     number;
  nombreCartera: string;
  enArchivo:     number;
  yaEnBaseDatos: number;
}

export interface ValidarCargaIvrResponse {
  totalFilas:             number;
  totalValidas:           number;
  totalErrores:           number;
  totalCodigoDesconocido: number;
  huellaArchivo:          string;
  preview:                FilaIvr[];
  desglosePorEstado:      ConteoEstado[];
  desglosePorCartera:     ConteoCartera[];
  codigosDesconocidos:    string[];
  fechaMin:               string | null;
  fechaMax:               string | null;
  solapamientos:          Solapamiento[];
  alertas:                AlertaSolicitud[];
  erroresAgrupados:       ErrorAgrupado[];
  columnasEncontradas:    string[];
  columnasFaltantes:      string[];
  filasRepetidasEnArchivo: number;
}

export interface ConfirmarCargaIvrResponse {
  insertados:  number;
  descartados: number;
  fechaCargue: string;
  mensaje:     string;
}
