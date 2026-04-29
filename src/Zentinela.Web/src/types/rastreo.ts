export interface RastreoTitularEnTabla {
  numeroDocumento: number;
  nombre:          string;
  apariciones:     number;
}

export interface RastreoTablaCard {
  tabla:       string;
  etiqueta:    string;
  apariciones: number;
  titulares:   RastreoTitularEnTabla[];
}

export interface RastreoTitularResumen {
  numeroDocumento:    number;
  nombre:             string;
  tablasDondeAparece: number;
  totalApariciones:   number;
  tablas:             string[];
}

export interface RastreoLinea {
  numeroBuscado:      string;
  totalApariciones:   number;
  titularesDistintos: number;
  porTabla:           RastreoTablaCard[];
  titulares:          RastreoTitularResumen[];
}
