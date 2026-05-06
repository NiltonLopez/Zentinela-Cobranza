export type EstadoValidacion = 'Ok' | 'Nuevo' | 'YaAsignado' | 'Error';

export interface NuevoTitularDto {
  numeroDocumento: number;
  nombre:          string;
}

export interface FilaValidacionDto {
  numeroDocumento: string;
  nombre:          string | null;
  estado:          EstadoValidacion;
  razon:           string | null;
}

export interface PreviewAsignacionDto {
  filas:             FilaValidacionDto[];   // primeras 10 filas sin error (vista previa)
  filasConError:     FilaValidacionDto[];   // TODAS las filas con error
  totalValidos:      number;                // Ok — existen en BD, listos
  totalNuevos:       number;                // Nuevo — se crearán en Titular
  totalErrores:      number;                // Error — fallaron validación
  titularIDsValidos: number[];
  nuevosTitulares:   NuevoTitularDto[];
}

export interface ConfirmarCargueRequest {
  carteraID:       number;
  liderID:         number;
  titularIDs:      number[];
  nuevosTitulares: NuevoTitularDto[];
}

export interface ResultadoCargueDto {
  insertados: number;
  duplicados: number;
  mensaje:    string;
}
