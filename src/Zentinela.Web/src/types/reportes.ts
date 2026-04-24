export type EstadoFilaReporte =
  | 'Ok'
  | 'NuevoTitular'
  | 'SinNombreParaCrear'
  | 'AgenteNoEncontrado';

export interface FilaReporteRaw {
  titularID?:            number | null;
  nombre?:               string | null;
  carteraID?:            number | null;
  usuarioControl?:       string | null;
  agenteID?:             number | null;
  ultimoPerfil?:         string | null;
  mejorPerfil?:          string | null;

  saldoTotal?:           number | null;
  numeroMarcado?:        string | null;
  rotacion?:             number | null;
  fechaUltimaGestion?:   string | null;
  fechaPromesa?:         string | null;
  valorPromesa?:         number | null;
  motivoNoPago?:         string | null;
  fechaActualizacion?:   string | null;
}

export interface FilaValidacionReporteDto {
  titularID:       string;
  nombre:          string | null;
  usuarioControl:  string;
  estado:          EstadoFilaReporte;
  razon:           string | null;
  camposMapeados:  string[];
}

export interface PreviewReporteDto {
  filas:                FilaValidacionReporteDto[];
  filasConError:        FilaValidacionReporteDto[];
  totalValidos:         number;
  totalNuevos:          number;
  totalInvalidos:       number;
  filasValidasRaw:      FilaReporteRaw[];
  columnasEncontradas:  string[];
  totalUnicos:          number;
  totalDuplicados:      number;
}

export interface ConfirmarReporteRequest {
  filasValidas: FilaReporteRaw[];
}

export interface ResultadoReporteDto {
  procesados:      number;
  titularesNuevos: number;
  duplicados:      number;
  mensaje:         string;
}
