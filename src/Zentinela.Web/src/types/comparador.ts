export interface CarteraDropdownDto {
  carteraID: number;
  nombreCartera: string;
}

export interface TitularComparativoDto {
  numeroDocumento: number;
  nombre: string;
  nombreOtraCartera: string;
  perfilSeleccionada: string | null;
  perfilOtraCartera: string | null;
  numeroMarcadoSeleccionada: string | null;
  numeroMarcadoOtra: string | null;
  saldoTotal: number | null;
  fechaUltimaGestion: string | null;
  valorPromesa: number | null;
}

export interface ComparadorVsCarteraDto {
  carteraID: number;
  nombreCartera: string;
  totalCoincidencias: number;
  preview: TitularComparativoDto[];
}

export interface ComparadorCarterasDto {
  carteraID: number;
  nombreCarteraSeleccionada: string;
  totalSinGestion: number;
  comparaciones: ComparadorVsCarteraDto[];
}
