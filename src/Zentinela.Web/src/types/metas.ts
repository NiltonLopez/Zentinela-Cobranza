export type TipoMeta = 0 | 1; // 0 = Recaudo, 1 = Honorarios

export interface FilaMetaLiderCarteraDto {
  liderID:              number;
  carteraID:            number;
  nombreLider:          string;
  nombreCartera:        string;
  capacidadActual:      number;
  cantidadAsignaciones: number;
  tipoMeta:             TipoMeta | null;
  valorMeta:            number | null;
  tieneMeta:            boolean;
}

export interface FilaMetaAgenteDto {
  agenteID:      number;
  nombreAgente:  string;
  nombreCartera: string | null;
  nombreLider:   string | null;
  tipoMeta:      TipoMeta | null;
  valorMeta:     number | null;
  tieneMeta:     boolean;
}

export interface EstadoMetasMesDto {
  año:                   number;
  mes:                   number;
  liderCarteras:         FilaMetaLiderCarteraDto[];
  liderCarterasExternas: FilaMetaLiderCarteraDto[];
  agentes:               FilaMetaAgenteDto[];
}

export interface GuardarMetaLiderRequest {
  liderID:   number;
  carteraID: number;
  año:       number;
  mes:       number;
  tipoMeta:  TipoMeta;
  valor:     number;
}

export interface GuardarMetaAgenteRequest {
  agenteID: number;
  año:      number;
  mes:      number;
  tipoMeta: TipoMeta;
  valor:    number;
}

export interface ResultadoMetaDto {
  actualizado: boolean;
  mensaje:     string;
}
