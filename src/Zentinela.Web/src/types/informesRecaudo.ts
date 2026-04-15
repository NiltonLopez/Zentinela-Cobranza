// ── Informe de Agentes ────────────────────────────────────────────────────────

export interface InformeAgenteMes {
  agenteID: number;
  nombreAgente: string;
  nombreLider: string;
  gestionado: number;
  recaudo: number;
  honorarios: number;
  meta: number;
  tieneMeta: boolean;
  deberiaLlevarHoy: number;
  faltaParaMeta: number;
  promedioPagoDia: number;
}

export interface InformeAgentesResponse {
  totalRecaudo: number;
  totalHonorarios: number;
  totalAgentes: number;
  totalGestionado: number;
  agentes: InformeAgenteMes[];
}

// ── Informe de Líderes ────────────────────────────────────────────────────────

export interface InformeLiderCartera {
  liderID: number;
  nombreLider: string;
  carteraID: number;
  nombreCartera: string;
  metaCartera: number;
  tieneMeta: boolean;
  capacidad: number;
  recaudo: number;
  honorarios: number;
  deberiaLlevarHoy: number;
  proyeccionCierre: number;
}

export interface InformeLideresResponse {
  totalRecaudo: number;
  totalMeta: number;
  totalLideres: number;
  liderCarteras: InformeLiderCartera[];
}

// ── Informe de Carteras (comparativo) ────────────────────────────────────────

export interface Periodo {
  año: number;
  mes: number;
  nombre: string;
}

export interface InformeCarteraComparativo {
  carteraID: number;
  nombreCartera: string;
  recaudoA: number;
  honorariosA: number;
  recaudoB: number;
  honorariosB: number;
  variacionRecaudo: number;
  porcentajeVariacion: number | null;
}

export interface InformeCarterasResponse {
  periodoA: Periodo;
  periodoB: Periodo;
  totalRecaudoA: number;
  totalRecaudoB: number;
  variacionTotal: number;
  carteras: InformeCarteraComparativo[];
}
