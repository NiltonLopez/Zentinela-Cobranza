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

/** Empleado de RRHH elegible para darse de alta como líder de cobranza. */
export interface EmpleadoElegible {
  numeroDocumento: number;
  nombreCompleto:  string;
  cargo:           string;
  email:           string | null;
}

/** Alta de un líder externo (no empleado). Documento y email son opcionales. */
export interface CrearLiderExternoRequest {
  nombre:           string;
  numeroDocumento?: number | null;
  email?:           string | null;
}

/** Resultado del alta de líder externo, con credenciales del usuario Zentinela creado. */
export interface CrearLiderExternoResponse {
  liderID:          number;
  nombre:           string;
  usuario:          string;
  /** Contraseña temporal generada (solo cuando no hay documento). null ⇒ la contraseña es el documento. */
  passwordTemporal: string | null;
}

export interface CarteraAutocomplete {
  id: number;
  nombreCartera: string;
}
