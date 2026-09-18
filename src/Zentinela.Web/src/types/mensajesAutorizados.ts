export interface MensajeAutorizado {
  id: number;
  titulo: string;
  texto: string;
  activo: boolean;
  longitud: number;
  longitudBase: number;
  fechaCreacion: string;
  usuarioCreacion: string;
  fechaActualizacion: string;
  usuarioActualizacion: string;
}

export interface GuardarMensajeAutorizado {
  titulo: string;
  texto: string;
  activo: boolean;
}

export interface CaracterRechazado {
  caracter: string;
  posicion: number; // base 1
}

export interface ValidacionMensaje {
  valido: boolean;
  mensaje?: string;
  caracteresRechazados?: CaracterRechazado[];
}

export const PLACEHOLDERS = ['{NOMBRE}', '{CARTERA}', '{EMPRESA}', '{CELULAR_ASESOR}', '{LINK_ASESOR}'] as const;
export const LIMITE_SMS = 160;
export const LONGITUD_CELULAR = 10;
/** Referencia para el contador: wa.me/57 + 10 dígitos. */
export const LONGITUD_LINK_WAME = 18;
