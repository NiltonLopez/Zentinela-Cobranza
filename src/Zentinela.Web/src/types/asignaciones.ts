export type EstadoValidacion = 'Ok' | 'NoExiste' | 'YaAsignado';

export interface FilaValidacionDto {
  numeroDocumento: string;
  nombre:          string | null;
  estado:          EstadoValidacion;
  razon:           string | null;
}

export interface PreviewAsignacionDto {
  filas:             FilaValidacionDto[];
  totalValidos:      number;
  totalInvalidos:    number;
  titularIDsValidos: number[];
}

export interface ConfirmarCargueRequest {
  carteraID:  number;
  liderID:    number;
  titularIDs: number[];
}

export interface ResultadoCargueDto {
  insertados: number;
  mensaje:    string;
}
