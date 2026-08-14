// ── Mensajería ────────────────────────────────────────────────────────────────

export interface FilaMensajeria {
  numeroDocumento: number;
  numeroTelefono:  string;
  nombreCartera:   string;
  liderID:         number;
  carteraID:       number;
  fechaEnvio:      string;
  esValida:        boolean;
  errorMensaje:    string | null;
}

export interface AlertaMensajeria {
  numeroTelefono: string;
  nombreCartera:  string;
  totalMensajes:  number;
}

export interface ValidarMensajeriaResponse {
  totalFilas:   number;
  totalValidas: number;
  totalErrores: number;
  preview:      FilaMensajeria[];
  filasValidas: FilaMensajeria[];
  alertas:      AlertaMensajeria[];
}

export interface MensajesPorLiderCartera {
  liderID:       number;
  nombreLider:   string;
  carteraID:     number;
  nombreCartera: string;
  total:         number;
}

export interface InformeMensajeria {
  totalMensajes:   number;
  porLiderCartera: MensajesPorLiderCartera[];
  alertas:         AlertaMensajeria[];
}

// ── IVR ───────────────────────────────────────────────────────────────────────

export interface FilaIVR {
  numeroDocumento: number;
  numeroTelefono:  string;
  nombreCartera:   string;
  liderID:         number;
  carteraID:       number;
  fechaEnvio:      string;
  esValida:        boolean;
  errorMensaje:    string | null;
}

export interface AlertaIVR {
  numeroTelefono: string;
  nombreCartera:  string;
  totalIVR:       number;
}

export interface ValidarIVRResponse {
  totalFilas:   number;
  totalValidas: number;
  totalErrores: number;
  preview:      FilaIVR[];
  filasValidas: FilaIVR[];
  alertas:      AlertaIVR[];
}

export interface IVRPorLiderCartera {
  liderID:       number;
  nombreLider:   string;
  carteraID:     number;
  nombreCartera: string;
  total:         number;
}

export interface InformeIVR {
  totalIVR:        number;
  porLiderCartera: IVRPorLiderCartera[];
  alertas:         AlertaIVR[];
}

// ── Análisis IVR (embudo y desenlaces, ADR-031) ───────────────────────────────
// Todos los conteos son de CLIENTES ÚNICOS por cartera: un cliente marcado varias
// veces cuenta una sola vez, con su mejor desenlace.

export interface EmbudoPasoIvr {
  etiqueta:   string;
  clientes:   number;
  porcentaje: number;
  base:       string;
}

export interface DesenlaceIvr {
  rango:      number;
  desenlace:  string;
  clientes:   number;
  porcentaje: number;
}

export interface AnalisisIvrCartera {
  carteraID:            number;
  nombreCartera:        string;
  clientesMarcados:     number;
  sinResultado:         number;
  clientesConResultado: number;
  contactados:          number;
  marcaronOpcion1:      number;
  contactabilidad:      number;
}

export interface AnalisisIvrLider {
  liderID:           number;
  nombreLider:       string;
  totalCarteras:     number;
  clientesMarcados:  number;
  /** Clientes sin resultado cargado (flujo viejo). Excluidos de la base de los %. */
  sinResultado:         number;
  clientesConResultado: number;
  contactados:       number;
  escucharonMenu:    number;
  marcaronOpcion1:   number;
  contactabilidad:   number;
  conversionOpcion1: number;
  embudo:            EmbudoPasoIvr[];
  desenlaces:        DesenlaceIvr[];
  porCartera:        AnalisisIvrCartera[];
  apreciacion:       string;
}

export interface AnalisisIvr {
  desde:             string;
  hasta:             string;
  totalMarcaciones:  number;
  clientesMarcados:  number;
  /** Clientes sin resultado cargado (flujo viejo). Excluidos de la base de los %. */
  sinResultado:         number;
  clientesConResultado: number;
  contactados:       number;
  escucharonMenu:    number;
  marcaronOpcion1:   number;
  contactabilidad:   number;
  conversionOpcion1: number;
  embudo:            EmbudoPasoIvr[];
  desenlaces:        DesenlaceIvr[];
  porLider:          AnalisisIvrLider[];
  totalLideres:      number;
  totalCarteras:     number;
  apreciacion:       string;
}

// ── Llamadas perdidas ─────────────────────────────────────────────────────────

export interface FilaLlamada {
  titularID:    number;
  celular:      string;
  nombreCartera:string;
  carteraID:    number;
  estado:       string;
}

export interface ValidarLlamadasResponse {
  totalFilas:     number;
  totalValidas:   number;
  totalInvalidas: number;
  preview:        FilaLlamada[];
  filasValidas:   FilaLlamada[];
}

export interface LlamadasPorCartera { nombreCartera: string; total: number; }
export interface AlertaLlamada      { celular: string; vecesComunicado: number; }
export interface LlamadaVsGestion   { titularID: number; celular: string; ultimoPerfil: string | null; fechaUltimaGestion: string | null; fechaPromesa: string | null; }

export interface InformeLlamadas {
  totalRegistros: number;
  porCartera:     LlamadasPorCartera[];
  alertas:        AlertaLlamada[];
  vsGestion:      LlamadaVsGestion[];
}

// ── Llamadas entrantes ────────────────────────────────────────────────────────

export interface FilaEntrante {
  celular:      string;
  titularID:    number | null;
  carteraID:    number | null;
  tieneAcuerdo: boolean | null;
  semana:       number;
  anio:         number;
}

export interface ValidarEntrantesResponse {
  totalCelulares:   number;
  totalResueltos:   number;
  totalConAcuerdo:  number;
  efectividad:      number;
  preview:          FilaEntrante[];
  filasParaGuardar: FilaEntrante[];
}

export interface EfectividadSemanal {
  anio: number; semana: number;
  totalCelulares: number; resueltos: number; conAcuerdo: number; efectividad: number;
}

export interface InformeEntrantes { porSemana: EfectividadSemanal[]; }

// ── Pagos vs Impacto ──────────────────────────────────────────────────────────

export interface PagoImpactoRow {
  cedulaTitular:       number;
  fechaPago:           string;
  valorPagado:         number;
  impactadoMensajeria: boolean;
  impactadoIVR:        boolean;
  impactadoLlamada:    boolean;
}

export interface ResumenImpacto {
  totalPagos:         number;
  pagosConMensajeria: number;
  pagosConIVR:        number;
  pagosConLlamada:    number;
  pagosSinImpacto:    number;
}

export interface PagosImpacto { filas: PagoImpactoRow[]; resumen: ResumenImpacto; }
