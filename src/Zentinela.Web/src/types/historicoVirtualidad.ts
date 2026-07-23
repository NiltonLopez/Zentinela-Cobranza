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
