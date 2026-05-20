export interface UsuarioAdmin {
  id: number;
  nombreCompleto: string;
  usuario: string;
  numeroDocumento: number;
  rol: string;
  requiereCambioPassword: boolean;
  nombreSupervisor: string | null;
}

export interface Supervisor {
  id: number;
  nombreCompleto: string;
  cargo: string;
  numeroDocumento: number;
}

export interface AgenteAdmin {
  agenteID: number;
  nombreCompleto: string;
  numeroDocumento: number;
  supervisorNombre: string | null;
  esLiderSupervisor: boolean;
  liderID: number | null;
  motivoNoAsignable: string | null;
  totalCarterasAsignadas: number;
}

export interface CarteraAgenteItem {
  carteraID: number;
  nombre: string;
  asignada: boolean;
}

export interface CarterasDelAgente {
  liderID: number | null;
  liderNombre: string | null;
  motivoNoAsignable: string | null;
  carteras: CarteraAgenteItem[];
}
