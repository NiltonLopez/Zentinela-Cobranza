import type { ErrorAgrupado } from './historicoVirtualidad';

// ── Paso 1 ──────────────────────────────────────────────────────────────────

export interface FilaSms {
  cedula: number;
  nombre: string;
  fechaGestion: string;
  ultimoPerfil: string;
  numeroMarcado: string;
}

export interface ValidarArchivoSmsResponse {
  huellaArchivo: string;
  huellaListaNegra: string | null;
  totalLeidas: number;
  totalValidas: number;
  excluidosCedulaInvalida: number;
  excluidosTelefonoInvalido: number;
  excluidosFijo: number;
  excluidosNombreVacio: number;
  excluidosPerfilArchivo: number;
  excluidosPerfilBD: number;
  excluidosListaNegraTabla: number;
  excluidosListaNegraArchivo: number;
  excluidosDuplicado: number;
  totalNumerosListaNegraArchivo: number;
  columnasEncontradas: string[];
  columnasFaltantes: string[];
  muestra: FilaSms[];
  erroresAgrupados: ErrorAgrupado[];
}

export interface CarteraListaNegra {
  valor: string;     // tal cual en BD (con '_'); es lo que se envía
  etiqueta: string;  // para mostrar
  totalNumeros: number;
}

// ── Paso 2 ──────────────────────────────────────────────────────────────────

export interface AsesorEstrategia {
  agenteID: number;
  nombre: string;
  numeroCorporativo: string | null;
  linkWhatsapp: string | null;
  precargado: boolean;
}

export interface ContactoAsesor {
  agenteID: number;
  numeroCorporativo: string;
  linkWhatsapp: string;
}

// ── Paso 3 ──────────────────────────────────────────────────────────────────

export type EstadoMensajeDisponible = 0 | 1 | 2; // Disponible | Advertencia | Deshabilitado

export interface TitularExcedido {
  cedula: number;
  nombre: string;
  longitud: number;
}

export interface MensajeDisponible {
  id: number;
  titulo: string;
  texto: string;
  estado: EstadoMensajeDisponible;
  motivo: string | null;
  longitudBase: number;
  linkMasLargo: number;
  longitudMaxima: number;
  totalExcedidos: number;
  excedidos: TitularExcedido[];
}

export interface CatalogoMensajesResponse {
  huellaArchivo: string;
  totalTitulares: number;
  linkMasLargo: number;
  mensajes: MensajeDisponible[];
}

// ── Paso 4 ──────────────────────────────────────────────────────────────────

export interface ResultadoGeneracion {
  estrategiaID: number;
  totalGenerados: number;
  excluidosLongitud: number;
  nombreArchivo: string;
}

/** Lo que viaja en cada endpoint con archivo. */
export interface EntradaSms {
  archivo: File;
  liderID: number | null;
  carterasListaNegra: string[];
  archivoListaNegra: File | null;
}
