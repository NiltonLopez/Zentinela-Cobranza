import type { InformeLiderCartera } from '../types/informesRecaudo';

/**
 * "cumplimiento": ganó por % de cumplimiento de meta (valor = fracción, ej. 1.05 = 105%).
 * Plan B (sin metas cargadas ese mes, o todas en 0%) — se destacan AMBOS criterios
 * de respaldo en paralelo, cada uno puede tener un líder ganador distinto:
 *   "honorarios-totales": mayor recaudo total de honorarios del mes (valor = pesos).
 *   "productividad":      mayor honorarios promedio por asesor a cargo (valor = pesos).
 * El consumidor debe rotular el mensaje distinto según el tipo para no insinuar que
 * hubo un cumplimiento de meta cuando en realidad no lo hubo.
 */
export type LiderDelMes =
  | { tipo: 'cumplimiento'; liderID: number; nombreLider: string; valor: number }
  | { tipo: 'honorarios-totales'; liderID: number; nombreLider: string; valor: number }
  | { tipo: 'productividad'; liderID: number; nombreLider: string; valor: number };

/**
 * Líder(es) del mes: primero por % de cumplimiento de meta (Honorarios ÷ ValorMeta,
 * promediado entre sus carteras, mismo cálculo que el Informe de Recaudo). Solo
 * cuentan líderes con cumplimiento > 0 — un líder con meta pero honorarios en $0
 * no debe "ganar" con un 0% vacío. Si hay ganador por cumplimiento, es el único
 * resultado (no se mezcla con el Plan B).
 *
 * Plan B (sin metas cargadas ese mes, o todos en 0%): honorarios totales del mes
 * Y honorarios promedio por asesor, cada uno con su propio ganador (pueden ser
 * líderes distintos). Solo cuentan líderes con honorarios > 0.
 *
 * Si tampoco hay honorarios de nadie ese mes, devuelve [] — no hay nada real
 * que celebrar todavía.
 */
export function calcularLiderDelMes(filas: InformeLiderCartera[]): LiderDelMes[] {
  const porCumplimiento = calcularPorCumplimiento(filas);
  if (porCumplimiento) return [porCumplimiento];

  return [calcularPorHonorariosTotales(filas), calcularPorProductividad(filas)]
    .filter((x): x is LiderDelMes => x !== null);
}

function calcularPorCumplimiento(filas: InformeLiderCartera[]): LiderDelMes | null {
  const porLider = new Map<number, { nombreLider: string; sumaCumplimiento: number; cantidad: number }>();

  for (const f of filas) {
    if (!f.tieneMeta || f.valorMeta <= 0) continue;
    const acc = porLider.get(f.liderID) ?? { nombreLider: f.nombreLider, sumaCumplimiento: 0, cantidad: 0 };
    acc.sumaCumplimiento += f.honorarios / f.valorMeta;
    acc.cantidad += 1;
    porLider.set(f.liderID, acc);
  }

  let mejor: { liderID: number; nombreLider: string; cumplimiento: number } | null = null;
  for (const [liderID, acc] of porLider) {
    const cumplimiento = acc.sumaCumplimiento / acc.cantidad;
    if (cumplimiento > 0 && (!mejor || cumplimiento > mejor.cumplimiento)) {
      mejor = { liderID, nombreLider: acc.nombreLider, cumplimiento };
    }
  }

  return mejor ? { tipo: 'cumplimiento', liderID: mejor.liderID, nombreLider: mejor.nombreLider, valor: mejor.cumplimiento } : null;
}

function calcularPorHonorariosTotales(filas: InformeLiderCartera[]): LiderDelMes | null {
  const porLider = new Map<number, { nombreLider: string; honorarios: number }>();

  for (const f of filas) {
    const acc = porLider.get(f.liderID) ?? { nombreLider: f.nombreLider, honorarios: 0 };
    acc.honorarios += f.honorarios;
    porLider.set(f.liderID, acc);
  }

  let mejor: { liderID: number; nombreLider: string; honorarios: number } | null = null;
  for (const [liderID, acc] of porLider) {
    if (acc.honorarios > 0 && (!mejor || acc.honorarios > mejor.honorarios)) {
      mejor = { liderID, nombreLider: acc.nombreLider, honorarios: acc.honorarios };
    }
  }

  return mejor ? { tipo: 'honorarios-totales', liderID: mejor.liderID, nombreLider: mejor.nombreLider, valor: mejor.honorarios } : null;
}

function calcularPorProductividad(filas: InformeLiderCartera[]): LiderDelMes | null {
  const porLider = new Map<number, { nombreLider: string; honorarios: number; asesores: number }>();

  for (const f of filas) {
    const acc = porLider.get(f.liderID) ?? { nombreLider: f.nombreLider, honorarios: 0, asesores: 0 };
    acc.honorarios += f.honorarios;
    acc.asesores += f.totalAsesores;
    porLider.set(f.liderID, acc);
  }

  let mejor: { liderID: number; nombreLider: string; promedio: number } | null = null;
  for (const [liderID, acc] of porLider) {
    if (acc.asesores <= 0) continue;
    const promedio = acc.honorarios / acc.asesores;
    if (promedio > 0 && (!mejor || promedio > mejor.promedio)) {
      mejor = { liderID, nombreLider: acc.nombreLider, promedio };
    }
  }

  return mejor ? { tipo: 'productividad', liderID: mejor.liderID, nombreLider: mejor.nombreLider, valor: mejor.promedio } : null;
}
