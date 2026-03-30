export interface LoginRequest {
  email: string;
  password: string;
}

export interface LoginResponse {
  token: string;
  nombreCompleto: string;
  rol: string;
  liderID: number | null;
  expiracion: string;
}

export interface Usuario {
  email: string;
  nombreCompleto: string;
  rol: string;
  liderID: number | null;
}

export type Rol = 'Admin' | 'Supervisor' | 'Líder' | 'Analista' | 'Asesor';
