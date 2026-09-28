import type { ErrorAgrupado } from './historicoVirtualidad';

// ── Paso 1 ──────────────────────────────────────────────────────────────────

export interface FilaSms {
  cedula: number;
  nombre: string;
  fechaGestion: string;
  ultimoPerfil: string;
  numeroMarcado: string;
  /** Vacíos si el archivo no trae la columna (opcional). */
  cartera: string;
  empresa: string;
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
  /** Fila de ejemplo de una plantilla vieja (cédula + número del ejemplo). */
  excluidosFilaEjemplo: number;
  totalNumerosListaNegraArchivo: number;
  columnasEncontradas: string[];
  columnasFaltantes: string[];
  columnasOpcionalesFaltantes: string[];
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

// La API serializa enums como texto (JsonStringEnumConverter en Program.cs).
export type EstadoMensajeDisponible = 'Disponible' | 'Advertencia' | 'Deshabilitado';

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
  variablesSinColumna: string[];
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

/**
 * Claves de motivo de exclusión. Espejo de CatalogoCreadorVirtualidad.Motivos: son el
 * puente entre el contador que se muestra como chip y el grupo de errores que se despliega
 * al hacerle clic. Si cambian del lado del servidor, el chip deja de encontrar su grupo y
 * se despliega vacío — por eso viven acá y no sueltas en el JSX.
 */
export const MOTIVOS = {
  cedulaInvalida:    'cedula-invalida',
  telefonoInvalido:  'telefono-invalido',
  fijo:              'fijo',
  nombreVacio:       'nombre-vacio',
  perfilArchivo:     'perfil-archivo',
  perfilBD:          'perfil-bd',
  listaNegraTabla:   'lista-negra-bd',
  listaNegraArchivo: 'lista-negra-archivo',
  duplicado:         'duplicado',
  filaEjemplo:       'fila-ejemplo',
  archivo:           'archivo',
} as const;
