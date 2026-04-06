export interface UsuarioAdmin {
  id: number;
  nombreCompleto: string;
  usuario: string;
  numeroDocumento: number;
  rol: string;
  requiereCambioPassword: boolean;
}
