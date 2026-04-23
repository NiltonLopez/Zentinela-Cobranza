export interface UsuarioAdmin {
  id: number;
  nombreCompleto: string;
  usuario: string;
  numeroDocumento: number;
  rol: string;
  requiereCambioPassword: boolean;
  supervisorNombre: string | null;
}

export interface Supervisor {
  id: number;
  nombreCompleto: string;
  cargo: string;
  numeroDocumento: number;
}
