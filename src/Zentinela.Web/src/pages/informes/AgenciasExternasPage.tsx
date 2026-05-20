import { useState, useMemo } from 'react';
import { useQuery } from '@tanstack/react-query';
import {
  ResponsiveContainer,
  BarChart,
  Bar,
  XAxis,
  YAxis,
  Tooltip,
  Legend,
  CartesianGrid,
} from 'recharts';
import { getInformeAgenciasExternas } from '../../api/informesRecaudoApi';
import type { FilaAgenciaExterna } from '../../types/informesRecaudo';

// ── Helpers ───────────────────────────────────────────────────────────────────

const cop = (v: number) =>
  new Intl.NumberFormat('es-CO', {
    style: 'currency', currency: 'COP', minimumFractionDigits: 0,
  }).format(v);

const MESES = [
  'Enero', 'Febrero', 'Marzo', 'Abril', 'Mayo', 'Junio',
  'Julio', 'Agosto', 'Septiembre', 'Octubre', 'Noviembre', 'Diciembre',
];

const hoy        = new Date();
const AÑO_ACTUAL = hoy.getFullYear();
const MES_ACTUAL = hoy.getMonth() + 1;

// Paleta cíclica para las carteras del gráfico apilado
const PALETA = ['#2563eb', '#16a34a', '#f59e0b', '#dc2626', '#7c3aed', '#ea580c', '#0891b2', '#db2777'];

// ── KPI card ──────────────────────────────────────────────────────────────────
function KpiCard({ label, value, sub }: { label: string; value: string; sub?: string }) {
  return (
    <div style={{
      background: 'var(--z-bg-card)', border: '1px solid var(--z-border)',
      borderRadius: 10, padding: '16px 20px', flex: 1, minWidth: 160,
    }}>
      <p style={{
        fontSize: 11, color: 'var(--z-text-muted)', marginBottom: 4,
        textTransform: 'uppercase', letterSpacing: '0.05em',
      }}>
        {label}
      </p>
      <p style={{ fontSize: 22, fontWeight: 600, color: 'var(--z-text-primary)', margin: 0 }}>
        {value}
      </p>
      {sub && <p style={{ fontSize: 12, color: 'var(--z-text-muted)', marginTop: 2 }}>{sub}</p>}
    </div>
  );
}

// ── Selector de período ───────────────────────────────────────────────────────
function SelectorPeriodo({ año, mes, onAño, onMes }: {
  año: number; mes: number;
  onAño: (n: number) => void; onMes: (n: number) => void;
}) {
  const baseStyle: React.CSSProperties = {
    padding: '6px 10px', borderRadius: 6, border: '1px solid var(--z-border)',
    background: 'var(--z-bg-card)', color: 'var(--z-text-primary)', fontSize: 13,
  };
  return (
    <div style={{ display: 'flex', alignItems: 'center', gap: 8 }}>
      <select value={mes} onChange={(e) => onMes(Number(e.target.value))} style={baseStyle}>
        {MESES.map((m, i) => (<option key={i} value={i + 1}>{m}</option>))}
      </select>
      <select value={año} onChange={(e) => onAño(Number(e.target.value))} style={baseStyle}>
        {[AÑO_ACTUAL - 1, AÑO_ACTUAL, AÑO_ACTUAL + 1].map((a) => (
          <option key={a} value={a}>{a}</option>
        ))}
      </select>
    </div>
  );
}

// ── Tooltip personalizado ─────────────────────────────────────────────────────
function TooltipBarras({ active, payload, label }: any) {
  if (!active || !payload?.length) return null;
  // Filtra segmentos con valor 0 para no listar carteras que no aporta el líder
  const items = payload.filter((p: any) => p.value > 0);
  if (items.length === 0) return null;
  const total = items.reduce((s: number, p: any) => s + p.value, 0);
  return (
    <div style={{
      background: 'var(--z-bg-card)', border: '1px solid var(--z-border)',
      borderRadius: 8, padding: '10px 14px', fontSize: 13, maxWidth: 320,
    }}>
      <p style={{ fontWeight: 600, marginBottom: 6, color: 'var(--z-text-primary)' }}>{label}</p>
      {items.map((p: any) => (
        <p key={p.name} style={{ color: p.color, margin: '2px 0' }}>
          {p.name}: <strong>{cop(p.value)}</strong>
        </p>
      ))}
      <p style={{
        marginTop: 6, paddingTop: 6, borderTop: '0.5px solid var(--z-border)',
        color: 'var(--z-text-muted)', fontSize: 12,
      }}>
        Total: <strong>{cop(total)}</strong>
      </p>
    </div>
  );
}

// ── Página ────────────────────────────────────────────────────────────────────
export function AgenciasExternasPage() {
  const [año, setAño] = useState(AÑO_ACTUAL);
  const [mes, setMes] = useState(MES_ACTUAL);

  const { data, isLoading, error } = useQuery({
    queryKey: ['agencias-externas', año, mes],
    queryFn:  () => getInformeAgenciasExternas(año, mes),
  });

  // Transforma filas para el gráfico: una entrada por líder con un campo por cartera externa.
  const { dataGrafico, carterasDistintas, filasPorLider } = useMemo(() => {
    const filas = data?.filas ?? [];

    // Agrupa por líder
    const porLider = new Map<number, { nombreLider: string; carteras: FilaAgenciaExterna[] }>();
    for (const f of filas) {
      if (!porLider.has(f.liderID)) porLider.set(f.liderID, { nombreLider: f.nombreLider, carteras: [] });
      porLider.get(f.liderID)!.carteras.push(f);
    }

    // Set ordenado de carteras únicas para tener dataKey estable en el gráfico
    const cartSet = new Set<string>();
    filas.forEach((f) => cartSet.add(f.nombreCartera));
    const carterasArr = Array.from(cartSet).sort((a, b) => a.localeCompare(b, 'es'));

    // Cada entrada del gráfico = un líder; cada cartera externa es una dataKey (0 si no aplica)
    const grafico = Array.from(porLider.values())
      .map(({ nombreLider, carteras }) => {
        const entrada: Record<string, string | number> = { nombreLider };
        for (const c of carterasArr) {
          const fila = carteras.find((x) => x.nombreCartera === c);
          entrada[c] = fila?.recaudo ?? 0;
        }
        return entrada;
      })
      // Ordena por total recaudo descendente para que las barras más grandes queden arriba
      .sort((a, b) => {
        const totA = carterasArr.reduce((s, c) => s + (a[c] as number), 0);
        const totB = carterasArr.reduce((s, c) => s + (b[c] as number), 0);
        return totB - totA;
      });

    return {
      dataGrafico:       grafico,
      carterasDistintas: carterasArr,
      filasPorLider:     porLider,
    };
  }, [data]);

  return (
    <div style={{ display: 'flex', flexDirection: 'column', gap: 24 }}>

      {/* Encabezado */}
      <div style={{ display: 'flex', alignItems: 'center', justifyContent: 'space-between', flexWrap: 'wrap', gap: 12 }}>
        <div>
          <h1 style={{ fontSize: 20, fontWeight: 600, color: 'var(--z-text-primary)', margin: 0 }}>
            Agencias Externas
          </h1>
          <p style={{ fontSize: 13, color: 'var(--z-text-muted)', margin: '4px 0 0' }}>
            Recaudo, honorarios y asignaciones por líder externo en {MESES[mes - 1]} {año}.
          </p>
        </div>
        <SelectorPeriodo año={año} mes={mes} onAño={setAño} onMes={setMes} />
      </div>

      {isLoading && (
        <p style={{ fontSize: 13, color: 'var(--z-text-muted)' }}>Cargando informe...</p>
      )}

      {error && (
        <div style={{
          background: '#fef2f2', border: '0.5px solid #fca5a5', borderRadius: 6,
          padding: '10px 14px', fontSize: 13, color: '#dc2626',
        }}>
          No se pudo cargar el informe. Reintenta más tarde.
        </div>
      )}

      {data && data.filas.length === 0 && (
        <div style={{
          background: 'var(--z-bg-card)', border: '0.5px solid var(--z-border)',
          borderRadius: 8, padding: 24, textAlign: 'center',
          fontSize: 14, color: 'var(--z-text-muted)',
        }}>
          No hay carteras externas registradas en el sistema.
        </div>
      )}

      {data && data.filas.length > 0 && (
        <>
          {/* KPIs */}
          <div style={{ display: 'flex', gap: 12, flexWrap: 'wrap' }}>
            <KpiCard label="Recaudo total"      value={cop(data.totalRecaudo)} />
            <KpiCard label="Honorarios totales" value={cop(data.totalHonorarios)} />
            <KpiCard label="Asignaciones"       value={data.totalAsignaciones.toLocaleString('es-CO')} />
            <KpiCard label="Agencias activas"   value={String(data.totalAgencias)} sub="líderes con cartera externa" />
          </div>

          {/* Gráfico de barras apiladas horizontales — recaudo por líder, segmentos por cartera */}
          <div style={{
            background: 'var(--z-bg-card)', border: '1px solid var(--z-border)',
            borderRadius: 10, padding: '20px 24px',
          }}>
            <p style={{
              fontSize: 13, fontWeight: 600, color: 'var(--z-text-primary)',
              margin: '0 0 12px', textTransform: 'uppercase', letterSpacing: '0.04em',
            }}>
              Recaudo por agencia externa
            </p>
            <ResponsiveContainer width="100%" height={Math.max(220, dataGrafico.length * 56)}>
              <BarChart layout="vertical" data={dataGrafico} margin={{ left: 16, right: 24 }}>
                <CartesianGrid strokeDasharray="3 3" horizontal={false} stroke="var(--z-border)" />
                <XAxis type="number" tickFormatter={(v) => cop(v)} fontSize={11} />
                <YAxis dataKey="nombreLider" type="category" width={140} fontSize={12} />
                <Tooltip content={<TooltipBarras />} />
                <Legend wrapperStyle={{ fontSize: 12 }} />
                {carterasDistintas.map((c, i) => (
                  <Bar
                    key={c}
                    dataKey={c}
                    stackId="recaudo"
                    fill={PALETA[i % PALETA.length]}
                    radius={i === carterasDistintas.length - 1 ? [0, 4, 4, 0] : 0}
                  />
                ))}
              </BarChart>
            </ResponsiveContainer>
          </div>

          {/* Tabla detalle agrupada por líder */}
          <div style={{
            background: 'var(--z-bg-card)', border: '0.5px solid var(--z-border)',
            borderRadius: 8, overflow: 'hidden',
          }}>
            <div style={{ overflowX: 'auto' }}>
              <table style={{ width: '100%', borderCollapse: 'collapse' }}>
                <thead>
                  <tr style={{ background: 'var(--z-bg-page)' }}>
                    {['Líder', 'Cartera', 'Asignaciones', 'Recaudo', 'Honorarios'].map((c, i) => (
                      <th key={i} style={{
                        padding: '10px 14px',
                        textAlign: i >= 2 ? 'right' : 'left',
                        fontSize: 11, fontWeight: 600, color: 'var(--z-text-muted)',
                        borderBottom: '0.5px solid var(--z-border)',
                        letterSpacing: '0.04em', textTransform: 'uppercase', whiteSpace: 'nowrap',
                      }}>
                        {c}
                      </th>
                    ))}
                  </tr>
                </thead>
                <tbody>
                  {Array.from(filasPorLider.entries()).flatMap(([liderID, grupo]) =>
                    grupo.carteras
                      .slice()
                      .sort((a, b) => b.recaudo - a.recaudo)
                      .map((f, idx) => (
                        <tr key={`${liderID}-${f.carteraID}`} style={{ borderBottom: '0.5px solid var(--z-border)' }}>
                          <td style={{ padding: '9px 14px', fontSize: 13, color: 'var(--z-text-primary)', fontWeight: idx === 0 ? 500 : 400 }}>
                            {idx === 0 ? grupo.nombreLider : ''}
                          </td>
                          <td style={{ padding: '9px 14px', fontSize: 13, color: 'var(--z-text-primary)' }}>
                            {f.nombreCartera}
                          </td>
                          <td style={{ padding: '9px 14px', fontSize: 13, color: 'var(--z-text-primary)', textAlign: 'right' }}>
                            {f.totalAsignaciones.toLocaleString('es-CO')}
                          </td>
                          <td style={{ padding: '9px 14px', fontSize: 13, color: 'var(--z-text-primary)', textAlign: 'right' }}>
                            {cop(f.recaudo)}
                          </td>
                          <td style={{ padding: '9px 14px', fontSize: 13, color: 'var(--z-text-primary)', textAlign: 'right' }}>
                            {cop(f.honorarios)}
                          </td>
                        </tr>
                      ))
                  )}
                </tbody>
              </table>
            </div>
          </div>
        </>
      )}
    </div>
  );
}
