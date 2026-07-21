// TEMPORAL — página de preview sin auth para visualizar la funcionalidad "Líder del mes"
// (cumplimiento de meta + Plan B de honorarios/productividad) con datos mock basados en
// el caso real de mayo 2026. Ver App.tsx: ruta /preview-lider-del-mes.
// REVERTIR antes de mergear: no debe quedar una ruta sin protección en producción.
import type { LiderDelMes } from '../../utils/liderDelMes';
import type { FilaLiderDto } from '../../types/dashboard';
import { BarraFraseLider } from './DashboardLiderPage';
import { BarraDestacadosLider } from './DashboardDirectorPage';

// Caso 1: hay cumplimiento de meta real (mes con metas cargadas).
const CUMPLIMIENTO: LiderDelMes[] = [
  { tipo: 'cumplimiento', liderID: 3, nombreLider: 'Julio Cesar Mora Leon', valor: 1.18 },
];

// Caso 2: Plan B — mayo 2026 sin metas cargadas (datos reales de la BD).
const PLAN_B: LiderDelMes[] = [
  { tipo: 'honorarios-totales', liderID: 3, nombreLider: 'Julio Cesar Mora Leon', valor: 156_419_815.76 },
  { tipo: 'productividad',      liderID: 4, nombreLider: 'Daniela Herrera',              valor: 14_085_820.69 },
];

const FILAS_MOCK: FilaLiderDto[] = [
  { liderID: 3, nombreLider: 'Julio Cesar Mora Leon', totalGestiones: 1200, tasaContactabilidad: 0.62, tasaEfectividad: 0.34, proyeccionPromesas: 180_000_000 },
  { liderID: 4, nombreLider: 'Daniela Herrera',              totalGestiones: 340,  tasaContactabilidad: 0.71, tasaEfectividad: 0.41, proyeccionPromesas: 50_000_000 },
  { liderID: 19, nombreLider: 'Lorena Patricia Suarez',     totalGestiones: 610,  tasaContactabilidad: 0.55, tasaEfectividad: 0.28, proyeccionPromesas: 42_000_000 },
];

function Seccion({ titulo, children }: { titulo: string; children: React.ReactNode }) {
  return (
    <div style={{ marginBottom: '56px' }}>
      <h2 style={{ fontSize: '16px', fontWeight: 700, marginBottom: '8px', color: 'var(--z-text-primary)' }}>{titulo}</h2>
      <div style={{ maxWidth: '1200px' }}>{children}</div>
    </div>
  );
}

export function PreviewLiderDelMesPage() {
  return (
    <div style={{ padding: '28px 32px' }}>
      <h1 style={{ fontSize: '20px', fontWeight: 700, marginBottom: '32px' }}>Preview — Líder del mes (mock)</h1>

      <Seccion titulo="Ticker Líder (BarraFraseLider) — caso con cumplimiento de meta, viendo su propio dashboard">
        <BarraFraseLider pct={72} liderDelMes={CUMPLIMIENTO} liderID={3} />
      </Seccion>

      <Seccion titulo="Ticker Líder (BarraFraseLider) — caso con cumplimiento de meta, viendo el dashboard de OTRO líder">
        <BarraFraseLider pct={72} liderDelMes={CUMPLIMIENTO} liderID={4} />
      </Seccion>

      <Seccion titulo="Ticker Líder (BarraFraseLider) — Plan B (mayo 2026, sin metas cargadas), líder ganador de productividad">
        <BarraFraseLider pct={72} liderDelMes={PLAN_B} liderID={4} />
      </Seccion>

      <Seccion titulo="Ticker Director (BarraDestacadosLider) — caso con cumplimiento de meta">
        <BarraDestacadosLider filas={FILAS_MOCK} liderDelMes={CUMPLIMIENTO} />
      </Seccion>

      <Seccion titulo="Ticker Director (BarraDestacadosLider) — Plan B (mayo 2026, sin metas cargadas)">
        <BarraDestacadosLider filas={FILAS_MOCK} liderDelMes={PLAN_B} />
      </Seccion>
    </div>
  );
}
