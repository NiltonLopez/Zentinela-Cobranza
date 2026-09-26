// Informes de Localización (ADR-040). Espeja Application/DTOs/InformesLocalizacion del backend.
// Porcentajes en escala 0–100 (null = sin universo). Fechas ISO.

import type { EstadoFuente, GrupoFuente } from './fuentesLocalizacion';

export interface CorteLocalizacion {
  corteID: number;
  /** "Localización al …". */
  fechaCorte: string;
  motivo: 'ARRANQUE' | 'CORTE_13H' | 'CARGUE_FUENTE' | 'MANUAL' | string;
  usuario: string | null;
  estado: 'EN CURSO' | 'OK' | 'ERROR' | string;
  duracionMs: number | null;
  pares: number | null;
  titulares: number | null;
  /** "Gestión al …": última carga OK del ETL de gestión cuando se calculó el corte. */
  datosGestionAl: string | null;
  error: string | null;
}

export interface EstadoLocalizacion {
  calculando: boolean;
  motivoEnCurso: string | null;
  inicioEnCurso: string | null;
  ultimoOk: CorteLocalizacion | null;
  ultimoIntento: CorteLocalizacion | null;
  vigente: boolean;
}

export interface MetricasLocalizacion {
  total: number;
  conContacto: number;
  sinContacto: number;
  conContactoFuncional: number;
  buscadosAlguna: number;
  buscadosFuncional: number;
  nuncaBuscadosFuncional: number;
  frescos180: number;
  conSaldo: number;
  sinContactoConSaldo: number;
  saldoSinContacto: number;
  saldoTotal: number;
  pctContacto: number | null;
  pctCobertura: number | null;
  pctFrescura: number | null;
  pctSinContactoConSaldo: number | null;
  indice: number | null;
}

export interface PesosIndice {
  contacto: number;
  cobertura: number;
  frescura: number;
}

export interface FuenteResumen {
  clave: string;
  etiqueta: string;
  slug: string;
  estado: EstadoFuente;
  grupo: GrupoFuente;
  nota: string | null;
  total: number;
  buscados: number;
  encontrados: number;
  noEncontrados: number;
  faltaPorBuscar: number;
  pctEncontrados: number | null;
  pctNoEncontrados: number | null;
  pctFaltaPorBuscar: number | null;
  ultimaConsultaMax: string | null;
  ultimoCargueOk: string | null;
}

export interface AlertaLocalizacion {
  tipo: 'POR_BUSCAR' | 'DATO_VIEJO' | 'SIN_CONTACTO_SALDO' | string;
  severidad: 'alta' | 'media' | string;
  mensaje: string;
  fuente: string | null;
  liderID: number | null;
  carteraID: number | null;
}

export type AlcanceLocalizacion = 'global' | 'lider' | 'cartera' | 'par';

export interface ResumenLocalizacion {
  corte: CorteLocalizacion;
  alcance: AlcanceLocalizacion;
  etiquetaAlcance: string;
  metricas: MetricasLocalizacion;
  fuentes: FuenteResumen[];
  alertas: AlertaLocalizacion[];
  pesos: PesosIndice;
}

export type AgruparCobertura = 'par' | 'cartera' | 'lider';

export interface FilaCobertura {
  grupo: string;
  etiquetaGrupo: string;
  liderID: number | null;
  nombreLider: string | null;
  documentoLider: number | null;
  carteraID: number | null;
  nombreCartera: string | null;
  /** Fila de un par concreto: admite la descarga por celda del Calificador (ADR-019). */
  esPar: boolean;
  fuente: FuenteResumen;
  pctCobertura: number | null;
  pctExito: number | null;
  pctLocalizacion: number | null;
}

export interface CoberturaLocalizacion {
  corte: CorteLocalizacion;
  agrupar: AgruparCobertura;
  filas: FilaCobertura[];
}

export type UmbralFrescura = 90 | 180 | 365;

export interface FilaFrescura {
  clave: string;
  etiqueta: string;
  estado: EstadoFuente;
  total: number;
  buscados: number;
  hasta90: number;
  de91a180: number;
  de181a365: number;
  mas365: number;
  viejos: number;
  pctViejos: number | null;
  ultimaConsultaMax: string | null;
  ultimoCargueOk: string | null;
}

export interface FrescuraLocalizacion {
  corte: CorteLocalizacion;
  alcance: AlcanceLocalizacion;
  etiquetaAlcance: string;
  umbral: UmbralFrescura;
  fuentes: FilaFrescura[];
}

export type CriterioRanking = 'Indice' | 'Contacto' | 'Cobertura' | 'Frescura' | 'SinContactoSaldo' | 'Uso' | 'Retorno';

/** Criterios Uso y Retorno (etapa 3): lo enviado a localización en la ventana. */
export interface EnviosRanking {
  lotes: number;
  /** Filas enviadas (titular × fuente). */
  enviados: number;
  cola: number | null;
  pctColaEnviada: number | null;
  buscados: number;
  pctRetorno: number | null;
  localizados: number;
}

export interface FilaRankingLider {
  posicion: number;
  liderID: number;
  nombre: string;
  /** Cédula para la foto; null sin documento. */
  documento: number | null;
  valor: number | null;
  /** Puntos contra el corte del día anterior; null sin comparación. */
  delta: number | null;
  total: number;
  indice: number | null;
  pctContacto: number | null;
  pctCobertura: number | null;
  pctFrescura: number | null;
  sinContactoConSaldo: number;
  saldoSinContacto: number;
  esPropio: boolean;
  /** Solo con los criterios Uso y Retorno. */
  envios: EnviosRanking | null;
}

export interface FilaRankingCartera {
  posicion: number;
  carteraID: number;
  nombre: string;
  liderID: number | null;
  lideres: number;
  valor: number | null;
  delta: number | null;
  total: number;
  indice: number | null;
  pctContacto: number | null;
  pctCobertura: number | null;
  pctFrescura: number | null;
  sinContactoConSaldo: number;
  saldoSinContacto: number;
  envios: EnviosRanking | null;
}

export interface RankingsLocalizacion {
  corte: CorteLocalizacion;
  corteAnterior: CorteLocalizacion | null;
  criterio: CriterioRanking;
  menorEsMejor: boolean;
  incluirExternas: boolean;
  lideres: FilaRankingLider[];
  totalLideres: number;
  miPosicion: number | null;
  recortado: boolean;
  carteras: FilaRankingCartera[];
  /** true para el Líder: son sus pares, no carteras completas. */
  carterasSonPropias: boolean;
  /** Uso y Retorno: días de la ventana de lotes. */
  ventanaDias: number | null;
  /** P. ej. "todavía no hay lotes enviados" (las listas vienen vacías). */
  aviso: string | null;
}

// ── Sugerencias (etapa 2) ─────────────────────────────────────────────────────

export type CategoriaSugerencia = 'NUNCA_BUSCADO' | 'NO_ENC_VIEJO' | 'ENC_VIEJO';

export type MotivoExclusionSugerencia =
  'YA_PAGO' | 'FALLECIDO' | 'NO_GESTIONAR' | 'RECLAMACION' | 'ACUERDO' | 'PROMESA' | 'ENVIADO' | 'SIN_SALDO';

export interface ParametrosSugerencias {
  /** 0 / null = cartera completa (solo Admin/Director; al Líder el servidor le fija el suyo). */
  liderID: number | null;
  carteraID: number;
  /** Claves de fuentes Funcionales; vacío = todas. */
  fuentes: string[];
  umbralDias: number;
  /** null = no sugerir "encontrado pero viejo". */
  umbralRefrescoDias: number | null;
  soloConSaldo: boolean;
  top: number;
  excluirEnviadosDias: number;
  incluirLocalizados: boolean;
}

export interface DesglosePrioridad {
  perfil: number;
  saldo: number;
  gestion: number;
  nuncaFuncional: number;
  nuncaFuente: number;
  noEncontradoViejo: number;
  /** Negativo (−100) si ya tiene contacto fresco. */
  contactoFresco: number;
}

export interface FilaSugerencia {
  titularID: number;
  nombre: string | null;
  carteraID: number;
  cartera: string;
  liderID: number;
  lider: string;
  saldo: number | null;
  ultimoPerfil: string | null;
  fechaUltimaGestion: string | null;
  fuente: string;
  etiquetaFuente: string;
  categoria: CategoriaSugerencia;
  /** Última fila del titular en la fuente (null = nunca buscado). */
  ultimaConsulta: string | null;
  prioridad: number;
  desglose: DesglosePrioridad;
  encontradoEn: string[];
}

export interface ConteoCategoriaSugerencia {
  categoria: CategoriaSugerencia;
  etiqueta: string;
  antes: number;
  candidatos: number;
}

export interface ConteoFuenteSugerencia {
  fuente: string;
  etiqueta: string;
  estado: EstadoFuente;
  /** = "Falta por buscar" del Calificador para el par. */
  nuncaAntes: number;
  noEncontradoViejoAntes: number;
  encontradoViejoAntes: number;
  excluidos: number;
  nunca: number;
  noEncontradoViejo: number;
  encontradoViejo: number;
  candidatos: number;
  conContactoFresco: number;
  saldoCandidatos: number;
}

export interface ConteoExclusionSugerencia {
  motivo: MotivoExclusionSugerencia;
  etiqueta: string;
  filas: number;
  titulares: number;
}

export interface KpisSugerencias {
  titularesPar: number;
  titularesConSaldo: number;
  p90Saldo: number | null;
  candidatosAntes: number;
  candidatos: number;
  titularesCandidatos: number;
  saldoCandidatos: number;
  conContactoFresco: number;
  porCategoria: ConteoCategoriaSugerencia[];
  porFuente: ConteoFuenteSugerencia[];
  exclusiones: ConteoExclusionSugerencia[];
}

export interface SugerenciasLocalizacion {
  liderID: number | null;
  carteraID: number;
  etiquetaAlcance: string;
  parametros: ParametrosSugerencias;
  datosGestionAl: string | null;
  horasDesdeCargaGestion: number | null;
  registroEnviosDisponible: boolean;
  avisos: string[];
  kpis: KpisSugerencias;
  filas: FilaSugerencia[];
  hayMas: boolean;
  duracionMs: number;
}

// ── Envíos a localización (etapa 3) ───────────────────────────────────────────

export type EstadoLote = 'BORRADOR' | 'ENVIADO' | 'CERRADO' | 'ANULADO';

export type AccionLote = 'enviar' | 'cerrar' | 'anular';

/** POST envios: los mismos parámetros de la consulta de sugerencias + la fuente del lote. */
export interface CrearEnvio extends ParametrosSugerencias {
  fuente: string;
  observacion?: string | null;
}

export interface EnvioCreado {
  envioID: number;
  /** LOC-000123. */
  codigo: string;
  titulares: number;
  avisos: string[];
}

export interface RetornoEnvio {
  /** CARGUE = hay un cargue OK de la fuente después del envío; FECHA = no, se cuenta todo dato posterior al envío. */
  base: 'CARGUE' | 'FECHA';
  cargueEjecucionID: number | null;
  fechaCargue: string | null;
  buscadosDesde: string;
  buscados: number;
  pctRetorno: number | null;
  localizados: number;
  pctLocalizadosBuscados: number | null;
  pctLocalizados: number | null;
  /** null = todavía no volvió nada. */
  fechaRetorno: string | null;
  diasRetorno: number | null;
}

export interface ImpactoEnvio {
  gestionados: number;
  pctGestionados: number | null;
  pasaronAContacto: number;
  titularesConPago: number;
  pagos: number;
  valorPagado: number;
  honorarios: number;
  promesasVigentes: number;
}

export interface EnvioLocalizacion {
  envioID: number;
  codigo: string;
  liderID: number;
  lider: string;
  documentoLider: number | null;
  carteraID: number;
  cartera: string;
  fuente: string;
  etiquetaFuente: string;
  estado: EstadoLote;
  titulares: number;
  parametros: string | null;
  observacion: string | null;
  creadoPor: string;
  fechaCreacion: string;
  enviadoPor: string | null;
  fechaEnvio: string | null;
  cerradoPor: string | null;
  fechaCierre: string | null;
  anuladoPor: string | null;
  fechaAnulacion: string | null;
  motivoAnulacion: string | null;
  retorno: RetornoEnvio | null;
  impacto: ImpactoEnvio | null;
}

export interface FilaDetalleEnvio {
  titularID: number;
  nombre: string | null;
  categoria: CategoriaSugerencia;
  prioridad: number;
  ultimaConsultaPrevia: string | null;
  saldoFoto: number | null;
  perfilFoto: string | null;
  gestionFoto: string | null;
  buscado: boolean;
  localizado: boolean;
  fechaDato: string | null;
  perfilActual: string | null;
  gestionActual: string | null;
  gestionado: boolean;
  pasoAContacto: boolean;
  valorPagado: number;
  promesaVigente: string | null;
}

export interface DetalleEnvioLocalizacion {
  envio: EnvioLocalizacion;
  filas: FilaDetalleEnvio[];
  pagina: number;
  tamPagina: number;
  totalFilas: number;
}

export interface FiltroEnvios {
  liderID?: number;
  carteraID?: number;
  fuente?: string;
  estado?: EstadoLote;
}

export interface ListaEnviosLocalizacion {
  envios: EnvioLocalizacion[];
  recortado: boolean;
}

export interface FilaUsoLider {
  /** 0 en la fila de total. */
  liderID: number;
  nombre: string;
  documento: number | null;
  lotes: number;
  borradores: number;
  enviados: number;
  cola: number | null;
  pctColaEnviada: number | null;
  buscados: number;
  pctRetorno: number | null;
  localizados: number;
  pctLocalizados: number | null;
  gestionados: number;
  pasaronAContacto: number;
  titularesConPago: number;
  valorPagado: number;
  honorarios: number;
  promesasVigentes: number;
  lotesConRetorno: number;
  diasRetornoPromedio: number | null;
}

export interface UsoDepartamento {
  desde: string;
  hasta: string;
  corteCola: string | null;
  lideres: FilaUsoLider[];
  total: FilaUsoLider;
  avisos: string[];
}

// ── Etapa 4: tendencia, asesores y alertas de la semana ──────────────────────

export interface SemanaTendencia {
  /** Lunes de la semana. */
  inicioSemana: string;
  corteID: number;
  fechaCorte: string;
  metricas: MetricasLocalizacion;
}

export interface PuntoFuenteTendencia {
  inicioSemana: string;
  total: number;
  buscados: number;
  encontrados: number;
  pctCobertura: number | null;
  pctLocalizacion: number | null;
}

export interface FuenteTendencia {
  clave: string;
  etiqueta: string;
  estado: EstadoFuente;
  puntos: PuntoFuenteTendencia[];
}

export interface TendenciaLocalizacion {
  alcance: AlcanceLocalizacion;
  etiquetaAlcance: string;
  semanasPedidas: number;
  /** Solo semanas con algún corte OK (el último de cada semana), de la más vieja a la más reciente. */
  semanas: SemanaTendencia[];
  fuentes: FuenteTendencia[];
  pesos: PesosIndice;
}

export type CriterioAsesor = 'Contactabilidad' | 'Gestion' | 'ConDato' | 'SinContactoSaldo';

export interface FilaAsesor {
  /** null = no entra al ranking (menos titulares que el mínimo). */
  posicion: number | null;
  /** Cédula (UltimoEstadoTitular.AgenteID): el documento de la foto. */
  documento: number;
  nombre: string;
  valor: number | null;
  delta: number | null;
  titulares: number;
  contactoEfectivo: number;
  pctContactoEfectivo: number | null;
  gestionadosRecientes: number;
  pctGestionadosRecientes: number | null;
  conDato: number;
  pctConDato: number | null;
  conSaldo: number;
  sinContactoConSaldo: number;
  pctSinContactoConSaldo: number | null;
  saldoSinContacto: number;
  carteras: number;
}

export interface AsesoresLocalizacion {
  corte: CorteLocalizacion;
  corteAnterior: CorteLocalizacion | null;
  alcance: AlcanceLocalizacion;
  etiquetaAlcance: string;
  criterio: CriterioAsesor;
  menorEsMejor: boolean;
  minTitulares: number;
  diasGestionReciente: number;
  asesores: FilaAsesor[];
  pocosTitulares: FilaAsesor[];
  titularesSinAsesor: number;
  titularesAlcance: number;
  aviso: string | null;
}

export type TipoAlertaSemana = 'CAIDA_INDICE' | 'LOTE_SIN_RETORNO' | 'FUENTE_SIN_CARGUE' | 'CARTERA_SIN_CONTACTO' | 'COLA_SIN_LOTES';

export interface AlertaSemana {
  tipo: TipoAlertaSemana | string;
  severidad: 'alta' | 'media' | string;
  mensaje: string;
  liderID: number | null;
  nombreLider: string | null;
  documentoLider: number | null;
  carteraID: number | null;
  envioID: number | null;
  fuente: string | null;
  magnitud: number | null;
}

export interface UmbralesAlertasSemana {
  caidaIndicePuntos: number;
  diasLoteSinRetorno: number;
  diasFuenteSinCargue: number;
  sinContactoConSaldoPct: number;
  colaGrande: number;
  diasSinLotes: number;
}

export interface AlertasSemana {
  /** Lunes de la semana actual. */
  semana: string;
  corte: CorteLocalizacion;
  corteSemanaAnterior: CorteLocalizacion | null;
  alertas: AlertaSemana[];
  avisos: string[];
  umbrales: UmbralesAlertasSemana;
}
