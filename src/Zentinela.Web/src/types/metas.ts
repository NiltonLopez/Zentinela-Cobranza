export interface FilaMetaLiderCarteraDto {
  liderID:              number;
  carteraID:            number;
  nombreLider:          string;
  nombreCartera:        string;
  capacidadActual:      number;
  cantidadAsignaciones: number;
  metaRecaudo:          number | null;
  metaHonorarios:       number | null;
  tieneMeta:            boolean;
}

export interface FilaMetaAgenteDto {
  agenteID:       number;
  nombreAgente:   string;
  nombreCartera:  string | null;
  nombreLider:    string | null;
  metaRecaudo:    number | null;
  metaHonorarios: number | null;
  tieneMeta:      boolean;
}

export interface EstadoMetasMesDto {
  año:           number;
  mes:           number;
  liderCarteras: FilaMetaLiderCarteraDto[];
  agentes:       FilaMetaAgenteDto[];
}

export interface GuardarMetaLiderRequest {
  liderID:        number;
  carteraID:      number;
  año:            number;
  mes:            number;
  metaRecaudo:    number;
  metaHonorarios: number;
}

export interface GuardarMetaAgenteRequest {
  agenteID:       number;
  año:            number;
  mes:            number;
  metaRecaudo:    number;
  metaHonorarios: number;
}

export interface ResultadoMetaDto {
  actualizado: boolean;
  mensaje:     string;
}
