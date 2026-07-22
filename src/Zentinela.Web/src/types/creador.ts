export interface ColumnaBaseMadre {
  nombre: string;
  etiqueta: string;
  tipo: 'Texto' | 'Numero' | 'Fecha';
}

export interface FiltroBaseMadreRequest {
  carteraID: number;
  /**
   * LiderID opcional. Solo lo respeta el backend para Admin/Director.
   * null = "Cartera Completo" (todos los líderes de la cartera).
   * Para rol Líder se ignora — el backend usa siempre su LiderID del JWT.
   */
  liderID?: number | null;
  ultimoPerfil?: string;
  mejorPerfil?: string;
  promesasVigentes?: boolean;
  rotacionMaxima?: number;
  saldoMinimo?: number;
  tipoAfiliado?: string;
  estadoFosyga?: string;
  fuentesCelular: string[];
  coincidenciasCelular: string[];
  fuentesEmail: string[];
  coincidenciasEmail: string[];
  columnasSeleccionadas: string[];
}

/** Precisión del total: Exacto | Aproximado (~N) | Minimo (más de N). */
export type TipoTotalPreview = 'Exacto' | 'Aproximado' | 'Minimo';

export interface PreviewBaseMadreResponse {
  total: number;
  totalTipo: TipoTotalPreview;
  filas: Record<string, unknown>[];
}

/**
 * Entrada del selector superior del Creador.
 *  - Líder: una entrada por cartera asignada, etiqueta = "NombreCartera".
 *  - Admin/Director: una entrada por (Cartera × Líder) + una agregada
 *    "NombreCartera Completo" con liderID = null.
 */
export interface OpcionCarteraDto {
  carteraID: number;
  liderID:   number | null;
  etiqueta:  string;
}
