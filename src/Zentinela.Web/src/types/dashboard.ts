// ── Director ──────────────────────────────────────────────────────────────────

export interface PagoDirectorDto {
  pagoID: number;
  cedulaTitular: number;
  nombreCliente: string;
  valorPagado: number;
  fechaPago: string;      // ISO date "YYYY-MM-DD"
  honorarios: number;
  nombreCartera: string;
  nombreLider: string;
  nombreAsesor: string;
  fechaRegistro: string;  // ISO datetime
}

export interface LiderDropdownDto {
  id: number;
  nombre: string;
}

export interface CarteraDropdownDto {
  id: number;
  nombreCartera: string;
}

export interface MetricasDirectorDto {
  fechaActualizacion: string | null;
  fechaDesde: string;  // ISO date "YYYY-MM-DD"
  fechaHasta: string;  // ISO date "YYYY-MM-DD"
  totalGestiones: number;
  totalAsignados: number;
  tasaContactabilidad: number;
  tasaEfectividad: number;
  proyeccionPromesas: number;
  totalIncumplidos: number;
}

export interface FilaLiderDto {
  liderID: number;
  nombreLider: string;
  totalGestiones: number;
  tasaContactabilidad: number;
  tasaEfectividad: number;
}

export interface FilaAsesorDto {
  agenteID: number;
  nombreAsesor: string;
  totalGestiones: number;
  tasaContactabilidad: number;
  tasaEfectividad: number;
}

export interface DashboardDirectorDto {
  metricas: MetricasDirectorDto;
  filasLider: FilaLiderDto[];
  filasAsesor: FilaAsesorDto[];
}

// ── Asesor ────────────────────────────────────────────────────────────────────

export interface AcuerdoVigenteDto {
  titularID: number;
  nombreTitular: string;
  ultimoPerfil: string;
  fechaPromesa: string; // ISO date "YYYY-MM-DD"
  valorPromesa: number | null;
  saldoTotal: number | null;
  semaforo: 'rojo' | 'amarillo' | 'verde';
}

export interface RendimientoAsesorDto {
  fechaActualizacion: string | null; // ISO datetime o null
  totalGestiones: number;
  tasaContactabilidad: number; // 0-1
  tasaEfectividad: number;     // 0-1
}

export interface PagoAsesorDto {
  pagoID: number;
  cedulaTitular: number;
  nombreCliente: string;
  valorPagado: number;
  fechaPago: string;      // ISO date "YYYY-MM-DD"
  honorarios: number;
  nombreCartera: string;
  fechaRegistro: string;  // ISO datetime
}

export interface PagosConfirmadosDto {
  fechaActualizacion: string | null; // ISO datetime o null
  totalRecaudo: number;
  totalHonorarios: number;
  metaHonorarios: number;
  porcentajeEfectividad: number; // 0-1
}

export interface DashboardAsesorDto {
  nombreSupervisor: string | null;
  acuerdosVigentes: AcuerdoVigenteDto[];
  rendimiento: RendimientoAsesorDto;
  pagosConfirmados: PagosConfirmadosDto;
}
