export interface ColumnaBaseMadre {
  nombre: string;
  etiqueta: string;
  tipo: 'Texto' | 'Numero' | 'Fecha';
}

export interface FiltroBaseMadreRequest {
  carteraID: number;
  ultimoPerfil?: string;
  mejorPerfil?: string;
  tieneFechaPromesa?: boolean;
  saldoMinimo?: number;
  tipoAfiliado?: string;
  estadoFosyga?: string;
  fuentesCelular: string[];
  coincidenciasCelular: string[];
  fuentesEmail: string[];
  coincidenciasEmail: string[];
  columnasSeleccionadas: string[];
}

export interface PreviewBaseMadreResponse {
  total: number;
  filas: Record<string, unknown>[];
}
