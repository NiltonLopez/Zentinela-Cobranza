export interface RelacionLiderCartera {
  liderID: number;
  carteraID: number;
  nombreLider: string;
  nombreCartera: string;
  totalAsignaciones: number;
}

export interface LiderDropdown {
  id: number;
  nombre: string;
}

export interface CarteraAutocomplete {
  id: number;
  nombreCartera: string;
}
