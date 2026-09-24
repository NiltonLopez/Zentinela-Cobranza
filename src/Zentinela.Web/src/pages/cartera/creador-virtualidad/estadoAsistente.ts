import type { ValidarArchivoSmsResponse, CatalogoMensajesResponse, ResultadoGeneracion } from '../../../types/creadorVirtualidad';

export type Paso = 1 | 2 | 3 | 4;

export interface FilaAsesor {
  /**
   * AgenteID real, o un NEGATIVO correlativo para las filas sueltas que no corresponden a
   * ningún agente (un número extra que el líder quiere sumar al reparto). El negativo sirve
   * de clave de React y de identidad para editar/eliminar, y al guardar contactos se filtran
   * por > 0: no hay a quién persistirle un número que no es de nadie.
   */
  agenteID: number;
  nombre: string;
  numeroCorporativo: string;
  linkWhatsapp: string;
  /** true = vino de Cartera_Agente del líder; false = agregado a mano ("prestado"). */
  precargado: boolean;
  /** Si es false, al escribir un móvil se regenera wa.me/57<numero>. Se vuelve true al editar el link. */
  linkEditadoManual: boolean;
  /** true = fila suelta sin agente detrás; su nombre es editable y no se persiste. */
  manual?: boolean;
}

export interface EstadoAsistente {
  paso: Paso;
  /** Líder: fijo al del usuario. Director/Admin: elegido del dropdown. */
  liderID: number | null;
  archivo: File | null;
  /** Valores CRUDOS (con '_'); la etiqueta es solo visual. */
  carterasListaNegra: string[];
  archivoListaNegra: File | null;
  validacion: ValidarArchivoSmsResponse | null;
  asesores: FilaAsesor[];
  catalogo: CatalogoMensajesResponse | null;
  mensajeID: number | null;
  generacion: ResultadoGeneracion | null;
}

/**
 * Número desde el que se distribuye: móvil (3…) o fijo nacional (6…), los dos de 10
 * dígitos. Espejo de CreadorVirtualidadSmsService.CelularRegex; la validación que manda
 * es la del servidor.
 */
export const CELULAR_REGEX = /^[36]\d{9}$/;

/**
 * Solo los móviles. El wa.me se propone únicamente para estos: un fijo puede tener
 * WhatsApp Business, pero no se asume — si el número empieza en 6 el líder pega el enlace
 * que corresponda en lugar de recibir un wa.me inventado.
 */
export const MOVIL_REGEX = /^3\d{9}$/;

export const linkWaMe = (numero: string) => `wa.me/57${numero}`;

/** Siguiente id negativo libre para una fila suelta (ver FilaAsesor.agenteID). */
export const siguienteIDManual = (asesores: FilaAsesor[]) =>
  Math.min(0, ...asesores.map(a => a.agenteID)) - 1;

export function estadoInicial(liderID: number | null): EstadoAsistente {
  return {
    paso: 1, liderID, archivo: null, carterasListaNegra: [], archivoListaNegra: null,
    validacion: null, asesores: [], catalogo: null, mensajeID: null, generacion: null,
  };
}

export type Accion =
  | { tipo: 'SET_LIDER'; liderID: number | null }
  | { tipo: 'SET_ARCHIVO'; archivo: File | null }
  | { tipo: 'SET_CARTERAS_LN'; carteras: string[] }
  | { tipo: 'SET_ARCHIVO_LN'; archivo: File | null }
  | { tipo: 'SET_VALIDACION'; validacion: ValidarArchivoSmsResponse }
  | { tipo: 'SET_ASESORES'; asesores: FilaAsesor[] }
  | { tipo: 'SET_CATALOGO'; catalogo: CatalogoMensajesResponse }
  | { tipo: 'SET_MENSAJE'; mensajeID: number | null }
  | { tipo: 'SET_GENERACION'; generacion: ResultadoGeneracion }
  | { tipo: 'IR_A'; paso: Paso }
  | { tipo: 'REINICIAR' };

/**
 * Invalidación en cascada: cada cambio borra lo que dependía de él. Cambiar el archivo
 * invalida la validación, el catálogo y el mensaje; cambiar los asesores invalida el
 * catálogo (el link más largo cambia el cálculo de los 160); cambiar el líder lo borra todo.
 */
export function reducer(estado: EstadoAsistente, accion: Accion): EstadoAsistente {
  switch (accion.tipo) {
    case 'SET_LIDER':
      return estadoInicial(accion.liderID);

    case 'SET_ARCHIVO':
      return { ...estado, archivo: accion.archivo, validacion: null, catalogo: null, mensajeID: null, generacion: null, paso: 1 };

    case 'SET_CARTERAS_LN':
      return { ...estado, carterasListaNegra: accion.carteras, validacion: null, catalogo: null, mensajeID: null, generacion: null, paso: 1 };

    case 'SET_ARCHIVO_LN':
      return { ...estado, archivoListaNegra: accion.archivo, validacion: null, catalogo: null, mensajeID: null, generacion: null, paso: 1 };

    case 'SET_VALIDACION':
      return { ...estado, validacion: accion.validacion };

    case 'SET_ASESORES':
      return { ...estado, asesores: accion.asesores, catalogo: null, mensajeID: null, generacion: null };

    case 'SET_CATALOGO':
      return { ...estado, catalogo: accion.catalogo, mensajeID: null, generacion: null };

    case 'SET_MENSAJE':
      return { ...estado, mensajeID: accion.mensajeID, generacion: null };

    case 'SET_GENERACION':
      return { ...estado, generacion: accion.generacion };

    case 'IR_A':
      return { ...estado, paso: accion.paso };

    case 'REINICIAR':
      return estadoInicial(estado.liderID);
  }
}

export const asesorValido = (a: FilaAsesor) =>
  CELULAR_REGEX.test(a.numeroCorporativo.trim()) && a.linkWhatsapp.trim().length > 0;

export const linkMasLargo = (asesores: FilaAsesor[]) =>
  asesores.reduce((max, a) => Math.max(max, a.linkWhatsapp.trim().length), 0);
