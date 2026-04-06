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
export type Rol = 'Admin' | 'Líder' | 'Director' | 'Asesor';
