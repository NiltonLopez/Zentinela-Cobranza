export interface RelacionLiderCartera {
  liderID: number;
  carteraID: number;
  nombreLider: string;
  nombreCartera: string;
  totalAsignaciones: number;
  totalAsesores: number;
  esExterna: boolean;
}

export interface LiderDropdown {
  id: number;
  nombre: string;
}

export interface CarteraAutocomplete {
  id: number;
  nombreCartera: string;
}
