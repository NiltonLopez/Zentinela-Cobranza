export interface PagoFila {
  cedulaTitular: number;
  nombreCliente: string;
  valorPagado: number;
  fechaPago: string;      // ISO date "YYYY-MM-DD"
  honorarios: number;
  usuarioControl: string;    // valor crudo del CSV — "BASE GENERAL" (case-insensitive) marca pago sin asesor
  agenteControlID: number | null;  // null cuando es BASE GENERAL
  liderID: number;           // resuelto desde TitularCartera (o CarteraAgente como desempate)
  esBaseGeneral: boolean;    // true cuando UsuarioControl = "BASE GENERAL"
  esDuplicado: boolean;      // true cuando el pago ya estaba cargado o se repite en el archivo — se omite
  esValida: boolean;
  errorMensaje?: string;
}

export interface ValidarPagosResponse {
  totalFilas: number;
  cantidadValidas: number;
  cantidadDuplicados: number;
  cantidadErrores: number;
  preview: PagoFila[];        // primeras 10 filas válidas (no duplicadas) para vista previa
  duplicados: PagoFila[];     // TODAS las filas ya cargadas / repetidas — se omiten
  errores: PagoFila[];        // TODAS las filas con error
  todasLasValidas: PagoFila[];
  todasLasInvalidas?: PagoFila[];
}

export interface CarteraDropdown {
  id: number;
  nombre: string;
}

// ── Pago individual ────────────────────────────────────────────────────────────

export interface RegistrarPagoIndividualRequest {
  carteraID: number;
  cedulaTitular: number;
  nombreCliente: string;
  valorPagado: number;
  fechaPago: string;            // "YYYY-MM-DD"
  honorarios: number;
  usuarioControl: string | null; // username del asesor; null/ignorado si esBaseGeneral
  esBaseGeneral: boolean;
}

export interface RegistrarPagoIndividualResponse {
  pagoID: number;
  liderID: number;
  liderNombre: string;
  agenteControlID: number | null;
  esBaseGeneral: boolean;
}

export interface AsesorCartera {
  agenteID: number;
  nombre: string;
  usuarioControl: string;
}

export interface TitularEnCartera {
  existe: boolean;
  nombre: string | null;
}
