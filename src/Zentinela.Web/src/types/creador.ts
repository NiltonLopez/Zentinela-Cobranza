export interface ColumnaBaseMadre {
  nombre: string;
  etiqueta: string;
  tipo: 'Texto' | 'Numero' | 'Fecha';
}

export interface FiltroBaseMadreRequest {
  carteraID: number;
  nombre?: string;
  ultimoPerfil?: string;
  mejorPerfil?: string;
  departamento?: string;
  municipio?: string;
  tieneFechaPromesa?: boolean;
  tieneSaldo?: boolean;
  columnasSeleccionadas: string[];
}

export interface PreviewBaseMadreResponse {
  total: number;
  filas: Record<string, unknown>[];
}
