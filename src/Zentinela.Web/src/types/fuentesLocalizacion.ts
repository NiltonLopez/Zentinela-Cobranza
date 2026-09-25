// Catálogo de fuentes de localización (ADR-040). Espeja FuenteLocalizacionDto del backend
// (CatalogoFuentesLocalizacion); los enums llegan como texto.

export type EstadoFuente = 'Funcional' | 'Intermitente' | 'Inactivo';

export type GrupoFuente = 'Contacto' | 'Afiliacion' | 'Laboral' | 'Vehiculo';

export interface FuenteLocalizacion {
  /** Clave del Cargue de Fuentes ("Fosyga", "DatosClaro"…). */
  clave: string;
  /** Nombre de la fila en "Porcentajes de Localización" del Calificador ("Claro", "ARL Sura"…). */
  etiquetaCalificador: string;
  /** Slug de la descarga por celda del Calificador (ADR-019). */
  slug: string;
  estado: EstadoFuente;
  grupo: GrupoFuente;
  nota: string | null;
  enPanelCalificador: boolean;
  /** ISO del fin del último cargue OK por el Cargue de Fuentes; null si nunca se cargó por ahí. */
  ultimoCargueOk: string | null;
}

/** Color del estado (mismos que usaba el Calificador). */
export const COLOR_ESTADO_FUENTE: Record<EstadoFuente, string> = {
  Funcional:    '#16A34A',
  Intermitente: '#CA8A04',
  Inactivo:     '#DC2626',
};
