export interface RelacionLiderCartera {
  liderID: number;
  carteraID: number;
  nombreLider: string;
  nombreCartera: string;
  totalAsignaciones: number;
  totalAsesores: number;
  esExterna: boolean;
  /** Relación activa (true) o retirada/inactivada (false). Ver ADR-022. */
  activo: boolean;
  /** True si tiene histórico (pagos/metas/mensajería/IVR) ⇒ la X inactiva en vez de eliminar. */
  tieneHistorico: boolean;
}

export interface LiderDropdown {
  id: number;
  nombre: string;
}

export interface CarteraAutocomplete {
  id: number;
  nombreCartera: string;
}
