export interface OpcionCarteraDto {
  carteraID: number;
  liderID: number | null;
  etiqueta: string;
}

export interface TitularComparativoDto {
  numeroDocumento: number;
  nombre: string;
  nombreOtraCartera: string;
  categoriaSeleccionada: string | null;
  perfilSeleccionada: string | null;
  categoriaOtraCartera: string | null;
  perfilOtraCartera: string | null;
  numeroMarcadoSeleccionada: string | null;
  numeroMarcadoOtra: string | null;
  saldoTotal: number | null;
  fechaUltimaGestion: string | null;
  fechaPromesa: string | null;
  valorPromesa: number | null;
}

export interface ComparadorVsCarteraDto {
  carteraID: number;
  nombreCartera: string;
  totalCoincidencias: number;
  preview: TitularComparativoDto[];
}

export interface CategoriaTotalDto {
  categoria: string;
  total: number;
}

export interface ComparadorCarterasDto {
  carteraID: number;
  nombreCarteraSeleccionada: string;
  totalBaseComparativo: number;
  desglosePorCategoria: CategoriaTotalDto[];
  comparaciones: ComparadorVsCarteraDto[];
}
