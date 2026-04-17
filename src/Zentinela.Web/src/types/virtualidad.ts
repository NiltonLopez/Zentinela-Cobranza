// ── Mensajería ────────────────────────────────────────────────────────────────

export interface FilaMensajeria {
  numeroDocumento: number;
  numeroTelefono:  string;
  nombreCartera:   string;
  carteraID:       number | null;
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

export interface MensajesPorCartera { nombreCartera: string; total: number; }

export interface InformeMensajeria {
  totalMensajes: number;
  porCartera:    MensajesPorCartera[];
  alertas:       AlertaMensajeria[];
}

// ── IVR ───────────────────────────────────────────────────────────────────────

export interface FilaIVR {
  titularID:   number;
  celular:     string;
  campana:     string | null;
  carteraID:   number | null;
  fechaEnvio:  string;
  resultado:   string | null;
  esValida:    boolean;
  errorMensaje:string | null;
}

export interface ValidarIVRResponse {
  totalFilas:   number;
  totalValidas: number;
  totalErrores: number;
  preview:      FilaIVR[];
  filasValidas: FilaIVR[];
}

export interface IVRPorSemana {
  anio: number; semana: number;
  total: number; contestaron: number; noContestaron: number;
}

export interface AlertaIVR { celular: string; carteraID: number | null; nombreCartera: string; totalIVR: number; }

export interface IVRVsGestion {
  titularID: number; celular: string; resultado: string | null;
  ultimoPerfil: string | null; fechaUltimaGestion: string | null;
  fechaPromesa: string | null; fechaEnvioIVR: string;
}

export interface InformeIVR {
  totalSolicitados:   number;
  totalContestaron:   number;
  totalNoContestaron: number;
  pctContesto:        number;
  pctNoContesto:      number;
  porSemana:          IVRPorSemana[];
  alertas:            AlertaIVR[];
  vsGestion:          IVRVsGestion[];
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
