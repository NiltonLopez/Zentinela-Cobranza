// Contratos del sub-módulo Informes → Auditoría de Gestiones.
// La UI consume estos tipos; el backend (fase posterior) los implementará tal cual.

/// Una fila de gestión proveniente del Excel estandarizado.
export interface FilaGestion {
  identificacion: string;
  nombreCompleto: string;
  asesorGestion: string;
  ultimoPerfil: string;
  contacto: string;
  valorPromesa: number | null;
  fechaPromesa: string | null;   // ISO yyyy-mm-dd
  gestion: string;
  estrategia?: string | null;
  tiempoGestion?: number | null; // duración de la gestión en minutos
  cartera: string;               // proviene del selector cartera–líder, no del archivo
  lider: string;                 // proviene del selector cartera–líder, no del archivo
}

/// Tipos de hallazgo detectables.
export type TipoIncongruencia =
  | 'perfil-gestion'    // último perfil no concuerda con el texto de la gestión (IA)
  | 'promesa-sin-valor' // hay fecha de promesa pero el valor está vacío
  | 'valor-sin-fecha'   // hay valor de promesa pero la fecha está vacía
  | 'valor-bajo';       // valor de promesa con menos de 5 dígitos

/// Niveles de severidad:
/// - critico:  inconsistencia entre último perfil y gestión (evaluada con IA)
/// - medio:    promesa incompleta (fecha sin valor, o valor sin fecha)
/// - bajo:     valor de promesa con menos de 5 dígitos
/// - revision: la IA no concluyó (Ambiguo) → requiere revisión manual
export type Severidad = 'critico' | 'medio' | 'bajo' | 'revision';

/// Un hallazgo concreto sobre una fila del archivo.
export interface IncongruenciaFila {
  fila: number;              // índice 1-based en el Excel
  identificacion: string;
  asesorGestion: string;
  lider: string;
  cartera: string;
  tipo: TipoIncongruencia;
  severidad: Severidad;
  detalle: string;           // explicación legible (regla o IA)
  ultimoPerfil: string;
  contacto: string;
  gestion: string;
  fechaPromesa: string | null;   // ISO yyyy-mm-dd
  valorPromesa: number | null;
}

/// Hallazgos agrupados por líder (para el listado del dashboard).
export interface GrupoLider {
  lider: string;
  items: IncongruenciaFila[];
}

/// Ranking de tipificaciones (último perfil) por cantidad de incongruencias.
export interface TipificacionRanking {
  tipificacion: string;   // el último perfil
  incongruencias: number;
  total: number;          // total de gestiones con esa tipificación
}

/// Agregados del dashboard.
export interface InformeAuditoria {
  totalGestiones: number;
  totalGestionesConTexto: number;     // gestiones con texto en el rango (universo IA)
  gestionesEvaluadasIA: number;       // las que realmente fueron a la IA (tope por costo)
  gestionesSinEvaluar: number;        // se enviaron a la IA pero su lote falló tras reintentar
  totalCriticas: number;              // inconsistencias perfil ↔ gestión (IA)
  totalMedias: number;                // promesas incompletas
  totalBajas: number;                 // valor de promesa < 5 dígitos
  totalRevision: number;              // Ambiguo (IA) → requiere revisión manual
  porcentajeIncongruencias: number;   // 0-100 (gestiones con cualquier hallazgo)
  duracionPromedioMin: number;        // minutos
  incongruenciasPorCartera: { cartera: string; incongruencias: number; total: number }[];
  palabraMasRepetida: { palabra: string; conteo: number; carteras: string[] };
  palabraInconsistencias: { palabra: string; conteo: number; carteras: string[] };
  topPalabras: { palabra: string; conteo: number }[];
  incongruenciasPorLider: GrupoLider[];
  tipificacionesConMasIncongruencias: TipificacionRanking[];
}

/// Respuesta del endpoint de análisis.
export interface AnalizarAuditoriaResponse {
  nombreArchivo: string;
  filasLeidas: number;
  informe: InformeAuditoria;
}

/// Reporte ya generado y disponible en memoria del servidor (compartido, expira a 48 h).
export interface ReporteGenerado {
  carteraID: number;
  dia: string;          // ISO yyyy-mm-dd
  generadoUtc: string;  // ISO datetime (UTC)
}

/// Estimación de costo (COP) de evaluar con IA las gestiones del filtro.
export interface EstimacionCosto {
  registrosIA: number;
  tokensEntrada: number;
  tokensSalida: number;
  costoEntrada: number;
  costoSalida: number;
  costoTotal: number;
}
