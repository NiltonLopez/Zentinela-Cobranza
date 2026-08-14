// Espejo de src/Zentinela.Application/DTOs/InformesGenerales/UsoBaseDatosDto.cs

export interface ArchivoProcesado {
  nombreArchivo: string;
  ok: boolean;
  mensaje?: string | null;
  /** Encabezado original del que se leyeron los teléfonos. */
  columnaDetectada?: string | null;
  filasLeidas: number;
  telefonosValidos: number;
  telefonosInvalidos: number;
  telefonosDistintos: number;
}

export interface UsoPorFuente {
  fuente: string;
  etiqueta: string;
  numerosDistintos: number;
  titularesDistintos: number;
  apariciones: number;
  porcentaje: number;
}

export interface UsoPorLider {
  /** null = fila agregada "(sin líder asignado)". */
  liderID: number | null;
  lider: string;
  numerosAcreditados: number;
  titularesDistintos: number;
  carterasDistintas: number;
  /** Titulares que el líder tiene asignados en Titular_Cartera (todas sus carteras). */
  titularesAsignados: number;
  relacionInactiva: boolean;
  sinRelacionLiderCartera: boolean;
  /** Cuota sobre el total encontrado. */
  porcentaje: number;
  /** Uso real de su base: titularesDistintos / titularesAsignados. null si no tiene asignación. */
  porcentajeCobertura: number | null;
}

export interface UsoPorLiderCartera {
  liderID: number | null;
  lider: string;
  carteraID: number | null;
  cartera: string;
  numerosAcreditados: number;
  titularesDistintos: number;
  /** Titulares asignados al líder EN ESA CARTERA. */
  titularesAsignados: number;
  porcentajeCobertura: number | null;
  esExterna: boolean;
  relacionInactiva: boolean;
  sinRelacionLiderCartera: boolean;
}

export interface UsoNumeroCompartido {
  numero: string;
  lideres: number;
  titulares: number;
  lideresNombres: string;
}

export interface UsoBaseDatos {
  archivos: ArchivoProcesado[];

  totalFilasLeidas: number;
  totalTelefonosValidos: number;
  totalTelefonosInvalidos: number;
  telefonosDistintos: number;
  solapamientoEntreArchivos: number;
  telefonosCelulares: number;
  telefonosFijos: number;

  numerosEnBaseDatos: number;
  numerosNoEncontrados: number;
  porcentajeUso: number;
  titularesDistintos: number;
  titularesHuerfanos: number;

  numerosConLider: number;
  numerosSinLider: number;
  numerosCompartidos: number;
  numerosUnSoloLider: number;
  lideresDistintos: number;
  /** Σ de porLider[].numerosAcreditados — ≥ numerosConLider por acreditación compartida. */
  sumaAcreditaciones: number;

  porFuente: UsoPorFuente[];
  porLider: UsoPorLider[];
  porLiderCartera: UsoPorLiderCartera[];
  topCompartidos: UsoNumeroCompartido[];
  muestraNoEncontrados: string[];

  advertencias: string[];
  duracionMs: number;
}
