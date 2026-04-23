export type EstadoFilaReporte =
  | 'Ok'
  | 'NuevoTitular'
  | 'SinNombreParaCrear'
  | 'UsuarioControlNextInvalido';

export interface FilaReporteRaw {
  titularID?:            number | null;
  nombre?:               string | null;
  carteraID?:            number | null; // inyectado desde la UI
  usuarioControlNext?:   string | null; // leído del Excel
  agenteID?:             number | null; // resuelto por lookup
  ultimoPerfil?:         string | null;
  mejorPerfil?:        string | null;
  fechaMejorPerfil?:   string | null;
  saldoTotal?:         number | null;
  numeroMarcado?:      string | null;
  rotacion?:           number | null;
  fechaUltimaGestion?: string | null;
  fechaPromesa?:       string | null;
  valorPromesa?:       number | null;
  motivoNoPago?:       string | null;
  fechaActualizacion?: string | null;
}

export interface FilaValidacionReporteDto {
  titularID:          string;
  nombre:             string | null;
  usuarioControlNext: string;
  estado:             EstadoFilaReporte;
  razon:              string | null;
}

export interface PreviewReporteDto {
  filas:           FilaValidacionReporteDto[];
  totalValidos:    number;
  totalNuevos:     number; // subconjunto de válidos — titulares que se crearán
  totalInvalidos:  number;
  filasValidasRaw: FilaReporteRaw[];
}

export interface ConfirmarReporteRequest {
  filasValidas: FilaReporteRaw[];
}

export interface ResultadoReporteDto {
  procesados:      number;
  titularesNuevos: number;
  mensaje:         string;
}
