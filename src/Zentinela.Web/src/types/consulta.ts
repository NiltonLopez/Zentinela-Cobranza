export interface CarteraInfo {
  nombreCartera: string;
  nombreLider: string;
}

export interface TitularInfo {
  nombre: string;
  numeroDocumento: string;
  fechaNacimiento: string | null;
  fechaExpedicionDocumento: string | null;
  carteras: CarteraInfo[];
}

export interface ConsultaResult {
  cedula: string;
  encontrado: boolean;
  fechaConsulta: string;
  titular: TitularInfo | null;
  /** Grupo → Fuente → Campo → Valor */
  datos: Record<string, Record<string, Record<string, string>>>;
}
