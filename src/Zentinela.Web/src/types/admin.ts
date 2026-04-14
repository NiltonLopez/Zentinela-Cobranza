export interface UsuarioAdmin {
  id: number;
  nombreCompleto: string;
  usuario: string;
  numeroDocumento: number;
  rol: string;
  requiereCambioPassword: boolean;
}

export interface Supervisor {
  id: number;
  nombreCompleto: string;
  cargo: string;
  numeroDocumento: number;
}
