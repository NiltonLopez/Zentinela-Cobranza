export interface PagoFila {
  cedulaTitular: number;
  nombreCliente: string;
  valorPagado: number;
  fechaPago: string;      // ISO date "YYYY-MM-DD"
  honorarios: number;
  usuarioControl: string;    // valor crudo del CSV — lookup por Agente.UsuarioControlNext
  agenteControlID: number;   // AgenteID resuelto del lookup
  liderID: number;           // resuelto automáticamente desde TitularCartera
  esValida: boolean;
  errorMensaje?: string;
}

export interface ValidarPagosResponse {
  totalFilas: number;
  cantidadValidas: number;
  cantidadErrores: number;
  preview: PagoFila[];        // primeras 10 filas válidas para vista previa
  errores: PagoFila[];        // TODAS las filas con error
  todasLasValidas: PagoFila[];
}

export interface CarteraDropdown {
  id: number;
  nombre: string;
}
