export interface LoginRequest {
  usuario: string;
  password: string;
}

export interface LoginResponse {
  token: string;
  email: string;
  nombreCompleto: string;
  rol: string;
  liderID: number | null;
  expiracion: string;
  requiereCambioPassword: boolean;
}

export interface Usuario {
  email: string;
  nombreCompleto: string;
  rol: string;
  liderID: number | null;
  requiereCambioPassword: boolean;
}

// Roles alineados con Application.Roles (backend)
// 'Líder Externo' y 'Asesor Externo' son agencias/personal externos: ven solo módulos
// operativos (Cartera para LiderExterno, Consulta Integrada para AsesorExterno) y
// quedan fuera de informes, metas, dashboards y demás flujos administrativos.
export type Rol =
  | 'Admin'
  | 'Líder'
  | 'Director'
  | 'Asesor'
  | 'Auxiliar de Sistemas'
  | 'Líder Externo'
  | 'Asesor Externo';
