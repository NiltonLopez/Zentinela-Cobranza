export type EstadoValidacion = 'Ok' | 'Nuevo' | 'YaAsignado' | 'Error';

export interface NuevoTitularDto {
  numeroDocumento: number;
  nombre:          string;
}

// Modo "Cartera Completo": par (TitularID, LiderID) a insertar. Cada fila del
// Excel trae su propio LiderID, por lo que se reparten titulares entre líderes.
export interface AsignacionValidaDto {
  titularID: number;
  liderID:   number;
}

export interface FilaValidacionDto {
  numeroDocumento: string;
  nombre:          string | null;
  estado:          EstadoValidacion;
  razon:           string | null;
}

export interface PreviewAsignacionDto {
  filas:               FilaValidacionDto[];   // primeras 10 filas sin error (vista previa)
  filasConError:       FilaValidacionDto[];   // TODAS las filas con error
  totalValidos:        number;                // Ok — existen en BD, listos
  totalNuevos:         number;                // Nuevo — se crearán en Titular
  totalErrores:        number;                // Error — fallaron validación
  titularIDsValidos:   number[];
  asignacionesValidas: AsignacionValidaDto[]; // solo en modo "Cartera Completo"
  nuevosTitulares:     NuevoTitularDto[];
}

export interface ConfirmarCargueRequest {
  carteraID:       number;
  liderID:         number | null;             // null = modo "Cartera Completo"
  titularIDs:      number[];
  asignaciones:    AsignacionValidaDto[];      // pares (TitularID, LiderID) en modo completo
  nuevosTitulares: NuevoTitularDto[];
}

export interface ResultadoCargueDto {
  insertados: number;
  duplicados: number;
  mensaje:    string;
}
