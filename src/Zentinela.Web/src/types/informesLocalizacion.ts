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

export type CriterioRanking = 'Indice' | 'Contacto' | 'Cobertura' | 'Frescura' | 'SinContactoSaldo';

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
