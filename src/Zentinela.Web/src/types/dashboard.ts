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

export interface DashboardAsesorDto {
  nombreSupervisor: string | null;
  acuerdosVigentes: AcuerdoVigenteDto[];
  rendimiento: RendimientoAsesorDto;
}
