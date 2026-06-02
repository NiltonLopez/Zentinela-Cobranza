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
