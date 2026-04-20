import { useState, useMemo } from 'react';
import { useQuery } from '@tanstack/react-query';
import {
  ResponsiveContainer,
  BarChart,
  Bar,
  XAxis,
  YAxis,
  CartesianGrid,
  Tooltip,
  Legend,
  ReferenceLine,
} from 'recharts';
import {
  getInformeAgentes,
  getInformeLideres,
  getInformeCarteras,
} from '../../api/informesRecaudoApi';
import type {
  InformeAgenteMes,
  InformeLiderCartera,
  InformeCarteraComparativo,
} from '../../types/informesRecaudo';

// ── Paleta ────────────────────────────────────────────────────────────────────
const C = {
  brand:      '#2563eb',
  meta:       '#94a3b8',
  proyeccion: '#f59e0b',
  honorarios: '#7c3aed',
  verde:      '#16a34a',
  rojo:       '#dc2626',
  naranja:    '#ea580c',
  mesA:       '#94a3b8',
  mesB:       '#2563eb',
};

// ── Helpers ───────────────────────────────────────────────────────────────────
const cop = (v: number) =>
  new Intl.NumberFormat('es-CO', {
    style: 'currency', currency: 'COP', minimumFractionDigits: 0,
  }).format(v);

const pct = (v: number | null) =>
  v === null ? '—' : `${v > 0 ? '+' : ''}${v.toFixed(1)}%`;

const MESES = [
  'Enero', 'Febrero', 'Marzo', 'Abril', 'Mayo', 'Junio',
  'Julio', 'Agosto', 'Septiembre', 'Octubre', 'Noviembre', 'Diciembre',
];

const hoy          = new Date();
const AÑO_ACTUAL   = hoy.getFullYear();
const MES_ACTUAL   = hoy.getMonth() + 1;
const MES_ANTERIOR = MES_ACTUAL === 1 ? 12 : MES_ACTUAL - 1;
const AÑO_ANTERIOR = MES_ACTUAL === 1 ? AÑO_ACTUAL - 1 : AÑO_ACTUAL;

// ── Tooltip personalizado ─────────────────────────────────────────────────────
function TooltipCOP({ active, payload, label }: any) {
  if (!active || !payload?.length) return null;
  return (
    <div style={{
      background: 'var(--z-bg-card)', border: '1px solid var(--z-border)',
      borderRadius: 8, padding: '10px 14px', fontSize: 13,
    }}>
      <p style={{ fontWeight: 600, marginBottom: 4, color: 'var(--z-text-primary)' }}>{label}</p>
      {payload.map((p: any) => (
        <p key={p.name} style={{ color: p.color, margin: '2px 0' }}>
          {p.name}: <strong>{cop(p.value)}</strong>
        </p>
      ))}
    </div>
  );
}

// ── KPI card ──────────────────────────────────────────────────────────────────
function KpiCard({ label, value, sub, color }: {
  label: string; value: string; sub?: string; color?: string;
}) {
  return (
    <div style={{
      background: 'var(--z-bg-card)', border: '1px solid var(--z-border)',
      borderRadius: 10, padding: '16px 20px', flex: 1, minWidth: 160,
    }}>
      <p style={{ fontSize: 11, color: 'var(--z-text-muted)', marginBottom: 4, textTransform: 'uppercase', letterSpacing: '0.05em' }}>
        {label}
      </p>
      <p style={{ fontSize: 22, fontWeight: 600, color: color ?? 'var(--z-text-primary)', margin: 0 }}>
        {value}
      </p>
      {sub && <p style={{ fontSize: 12, color: 'var(--z-text-muted)', marginTop: 2 }}>{sub}</p>}
    </div>
  );
}

// ── Selector de período ───────────────────────────────────────────────────────
function SelectorPeriodo({ año, mes, onAño, onMes, label }: {
  año: number; mes: number;
  onAño: (v: number) => void; onMes: (v: number) => void;
  label?: string;
}) {
  const sel: React.CSSProperties = {
    background: 'var(--z-bg-card)', border: '1px solid var(--z-border)',
    borderRadius: 6, padding: '5px 10px', fontSize: 13,
    color: 'var(--z-text-primary)', cursor: 'pointer',
  };
  return (
    <div style={{ display: 'flex', alignItems: 'center', gap: 8 }}>
      {label && <span style={{ fontSize: 13, color: 'var(--z-text-muted)' }}>{label}</span>}
      <select value={mes} onChange={e => onMes(Number(e.target.value))} style={sel}>
        {MESES.map((m, i) => <option key={i + 1} value={i + 1}>{m}</option>)}
      </select>
      <select value={año} onChange={e => onAño(Number(e.target.value))} style={sel}>
        {[2024, 2025, 2026, 2027].map(a => <option key={a} value={a}>{a}</option>)}
      </select>
    </div>
  );
}

// ── Semáforo de cumplimiento ──────────────────────────────────────────────────
function Semaforo({ recaudo, meta, deberiaHoy }: {
  recaudo: number; meta: number; deberiaHoy: number;
}) {
  if (meta === 0) return <span style={{ color: 'var(--z-text-muted)', fontSize: 12 }}>—</span>;
  const pctCump = (recaudo / meta) * 100;
  const pctHoy  = deberiaHoy > 0 ? (recaudo / deberiaHoy) * 100 : 100;
  const color   = pctHoy >= 90 ? C.verde : pctHoy >= 70 ? C.naranja : C.rojo;
  return (
    <span style={{ color, fontWeight: 600, fontSize: 13 }}>{pctCump.toFixed(1)}%</span>
  );
}

function Cargando() {
  return (
    <div style={{ textAlign: 'center', padding: '48px 0', color: 'var(--z-text-muted)', fontSize: 13 }}>
      Cargando datos…
    </div>
  );
}

function ErrorMsg() {
  return (
    <div style={{ textAlign: 'center', padding: '48px 0', color: C.rojo, fontSize: 13 }}>
      Error al cargar el informe. Verifica la conexión e inténtalo de nuevo.
    </div>
  );
}

// ── Ordenamiento de tablas ────────────────────────────────────────────────────
type SortDir = 'asc' | 'desc';

function useSortable<T extends Record<string, unknown>>(items: T[]) {
  const [key,  setKey]  = useState<keyof T | null>(null);
  const [dir,  setDir]  = useState<SortDir>('asc');

  const handleSort = (k: keyof T) => {
    if (key === k) setDir(d => d === 'asc' ? 'desc' : 'asc');
    else { setKey(k); setDir('asc'); }
  };

  const sorted = useMemo(() => {
    if (!key) return items;
    return [...items].sort((a, b) => {
      const va = a[key], vb = b[key];
      const cmp = typeof va === 'string'
        ? (va as string).localeCompare(vb as string, 'es')
        : (va as number) - (vb as number);
      return dir === 'asc' ? cmp : -cmp;
    });
  }, [items, key, dir]);

  return { sorted, sortKey: key, sortDir: dir, handleSort };
}

function ThSort({ label, k, sortKey, sortDir, onSort, align = 'right' }: {
  label: string; k: string;
  sortKey: string | null; sortDir: SortDir;
  onSort: (k: string) => void;
  align?: 'left' | 'right' | 'center';
}) {
  const active = sortKey === k;
  return (
    <th
      onClick={() => onSort(k)}
      style={{
        ...TH, textAlign: align, cursor: 'pointer', userSelect: 'none',
        color: active ? 'var(--z-text-primary)' : 'var(--z-text-muted)',
        whiteSpace: 'nowrap',
      }}
    >
      {label}&nbsp;
      <span style={{ fontSize: 10, opacity: active ? 1 : 0.25 }}>
        {active ? (sortDir === 'asc' ? '▲' : '▼') : '▲▼'}
      </span>
    </th>
  );
}

// ── Estilos base para th/td ───────────────────────────────────────────────────
const TH: React.CSSProperties = {
  padding: '10px 12px', color: 'var(--z-text-muted)', fontWeight: 500, whiteSpace: 'nowrap',
};
const TD: React.CSSProperties = {
  padding: '9px 12px', borderBottom: '1px solid var(--z-border)', whiteSpace: 'nowrap',
};

// ── Select de filtro ──────────────────────────────────────────────────────────
function FiltroSelect({ label, value, onChange, options, placeholder }: {
  label: string;
  value: string;
  onChange: (v: string) => void;
  options: { value: string; label: string }[];
  placeholder: string;
}) {
  const sel: React.CSSProperties = {
    background: 'var(--z-bg-card)', border: '1px solid var(--z-border)',
    borderRadius: 6, padding: '5px 10px', fontSize: 13,
    color: 'var(--z-text-primary)', cursor: 'pointer', minWidth: 160,
  };
  return (
    <div style={{ display: 'flex', alignItems: 'center', gap: 6 }}>
      <span style={{ fontSize: 12, color: 'var(--z-text-muted)', whiteSpace: 'nowrap' }}>{label}</span>
      <select value={value} onChange={e => onChange(e.target.value)} style={sel}>
        <option value="">{placeholder}</option>
        {options.map(o => <option key={o.value} value={o.value}>{o.label}</option>)}
      </select>
    </div>
  );
}

// ╔══════════════════════════════════════════════════════════════════════════════╗
// ║  TAB — AGENTES                                                              ║
// ╚══════════════════════════════════════════════════════════════════════════════╝
function TabAgentes({ año, mes }: { año: number; mes: number }) {
  const [filtroLider,   setFiltroLider]   = useState('');
  const [filtroCartera, setFiltroCartera] = useState('');
  const [busqueda,      setBusqueda]      = useState('');

  const { data, isLoading, isError } = useQuery({
    queryKey: ['inf-agentes', año, mes],
    queryFn:  () => getInformeAgentes(año, mes),
  });

  if (isLoading) return <Cargando />;
  if (isError || !data) return <ErrorMsg />;

  // Opciones únicas para los dropdowns
  const optsLider = Array.from(
    new Map(data.agentes.map(a => [String(a.liderID), a.nombreLider])).entries()
  ).map(([value, label]) => ({ value, label }));

  const optsCartera = Array.from(
    new Map(
      data.agentes
        .filter(a => !filtroLider || String(a.liderID) === filtroLider)
        .map(a => [a.nombreCartera, a.nombreCartera])
    ).entries()
  ).map(([value, label]) => ({ value, label }));

  // Filtrado cliente-lado
  const termino = busqueda.trim().toLowerCase();
  const agentesVisibles = data.agentes.filter(a => {
    if (filtroLider   && String(a.liderID) !== filtroLider)   return false;
    if (filtroCartera && a.nombreCartera   !== filtroCartera) return false;
    if (termino       && !a.nombreAgente.toLowerCase().includes(termino)) return false;
    return true;
  });

  const chartData = agentesVisibles.map(a => ({
    nombre:          a.nombreAgente,
    honorarios:      a.honorarios,
    deberiaLlevarHoy: a.deberiaLlevarHoy,
    metaHonorarios:  a.metaHonorarios,
  }));

  const totalRecaudo    = agentesVisibles.reduce((s, a) => s + a.recaudo, 0);
  const totalHonorarios = agentesVisibles.reduce((s, a) => s + a.honorarios, 0);
  const totalGestionado = agentesVisibles.reduce((s, a) => s + a.gestionado, 0);

  return (
    <div style={{ display: 'flex', flexDirection: 'column', gap: 20 }}>

      {/* KPIs */}
      <div style={{ display: 'flex', gap: 12, flexWrap: 'wrap' }}>
        <KpiCard label="Total Recaudo"        value={cop(totalRecaudo)}    color={C.brand}      />
        <KpiCard label="Total Honorarios"     value={cop(totalHonorarios)} color={C.honorarios} />
        <KpiCard label="Agentes"              value={String(agentesVisibles.length)} />
        <KpiCard label="Cuentas Gestionadas"  value={totalGestionado.toLocaleString('es-CO')} />
      </div>

      {/* Gráfico horizontal — menor a mayor */}
      <div style={{ background: 'var(--z-bg-card)', border: '1px solid var(--z-border)', borderRadius: 10, padding: '20px 16px' }}>

        {/* Cabecera gráfico + filtros */}
        <div style={{ display: 'flex', justifyContent: 'space-between', alignItems: 'center', flexWrap: 'wrap', gap: 12, marginBottom: 16 }}>
          <p style={{ fontSize: 13, fontWeight: 600, color: 'var(--z-text-primary)', margin: 0 }}>
            Recaudo por agente — ordenado de menor a mayor
          </p>
          <div style={{ display: 'flex', gap: 10, flexWrap: 'wrap', alignItems: 'center' }}>
            <FiltroSelect
              label="Líder:"
              value={filtroLider}
              onChange={v => { setFiltroLider(v); setFiltroCartera(''); }}
              options={optsLider}
              placeholder="Todos los líderes"
            />
            <FiltroSelect
              label="Cartera:"
              value={filtroCartera}
              onChange={setFiltroCartera}
              options={optsCartera}
              placeholder="Todas las carteras"
            />
            <div style={{ display: 'flex', alignItems: 'center', gap: 6 }}>
              <span style={{ fontSize: 12, color: 'var(--z-text-muted)', whiteSpace: 'nowrap' }}>Asesor:</span>
              <input
                type="text"
                placeholder="Buscar asesor…"
                value={busqueda}
                onChange={e => setBusqueda(e.target.value)}
                style={{
                  background: 'var(--z-bg-card)', border: '1px solid var(--z-border)',
                  borderRadius: 6, padding: '5px 10px', fontSize: 13,
                  color: 'var(--z-text-primary)', minWidth: 150, outline: 'none',
                }}
              />
            </div>
          </div>
        </div>

        {agentesVisibles.length === 0
          ? <p style={{ textAlign: 'center', color: 'var(--z-text-muted)', fontSize: 13, padding: '32px 0' }}>Sin agentes para el filtro seleccionado.</p>
          : (
            <ResponsiveContainer width="100%" height={Math.max(220, agentesVisibles.length * 44)}>
              <BarChart layout="vertical" data={chartData} margin={{ left: 10, right: 50, top: 4, bottom: 4 }}>
                <CartesianGrid horizontal={false} stroke="var(--z-border)" />
                <XAxis type="number" tickFormatter={v => cop(v)} tick={{ fontSize: 11, fill: 'var(--z-text-muted)' }} />
                <YAxis type="category" dataKey="nombre" width={160} tick={{ fontSize: 11, fill: 'var(--z-text-primary)' }} />
                <Tooltip content={<TooltipCOP />} />
                <Legend wrapperStyle={{ fontSize: 12 }} />
                <Bar dataKey="honorarios"       name="Honorarios"       fill={C.honorarios} radius={[0, 4, 4, 0]} barSize={10} />
                <Bar dataKey="deberiaLlevarHoy" name="Debería hoy"      fill={C.proyeccion} radius={[0, 4, 4, 0]} barSize={10} />
                <Bar dataKey="metaHonorarios"   name="Meta Honorarios"  fill={C.meta}       radius={[0, 4, 4, 0]} barSize={10} />
              </BarChart>
            </ResponsiveContainer>
          )
        }
      </div>

      {/* Tabla */}
      <TablaAgentes agentes={agentesVisibles} />
    </div>
  );
}

function TablaAgentes({ agentes }: { agentes: InformeAgenteMes[] }) {
  type Row = InformeAgenteMes & { cumplimiento: number };
  const rows: Row[] = agentes.map(a => ({
    ...a, cumplimiento: a.meta > 0 ? a.recaudo / a.meta : 0,
  })) as unknown as Row[];

  const { sorted, sortKey, sortDir, handleSort } = useSortable(rows as unknown as Record<string, unknown>[]);
  const th = (label: string, k: string, align?: 'left' | 'right' | 'center') => (
    <ThSort label={label} k={k} sortKey={sortKey as string | null} sortDir={sortDir} onSort={handleSort} align={align} />
  );

  return (
    <div style={{ background: 'var(--z-bg-card)', border: '1px solid var(--z-border)', borderRadius: 10, overflow: 'hidden' }}>
      <div style={{ overflowX: 'auto' }}>
        <table style={{ width: '100%', borderCollapse: 'collapse', fontSize: 13 }}>
          <thead>
            <tr style={{ background: 'var(--z-bg-page)', borderBottom: '1px solid var(--z-border)' }}>
              <th style={{ ...TH, textAlign: 'center' }}>#</th>
              {th('Agente',       'nombreAgente',    'left')}
              {th('Líder',        'nombreLider',     'left')}
              {th('Cartera',      'nombreCartera',   'left')}
              {th('Gestionado',   'gestionado',      'center')}
              {th('Recaudo',        'recaudo')}
              {th('Honorarios',     'honorarios')}
              {th('Meta Recaudo',   'meta')}
              {th('Meta Hon.',      'metaHonorarios')}
              {th('Debería hoy',    'deberiaLlevarHoy')}
              {th('Falta',        'faltaParaMeta')}
              {th('Prom./día',    'promedioPagoDia')}
              {th('Cump.',        'cumplimiento',    'center')}
            </tr>
          </thead>
          <tbody>
            {(sorted as unknown as Row[]).map((a, i) => <FilaAgente key={a.agenteID} a={a} pos={i + 1} />)}
          </tbody>
        </table>
      </div>
    </div>
  );
}

function FilaAgente({ a, pos }: { a: InformeAgenteMes; pos: number }) {
  return (
    <tr>
      <td style={{ ...TD, textAlign: 'center', color: 'var(--z-text-muted)' }}>{pos}</td>
      <td style={{ ...TD, textAlign: 'left', fontWeight: 500, color: 'var(--z-text-primary)' }}>{a.nombreAgente}</td>
      <td style={{ ...TD, textAlign: 'left', color: 'var(--z-text-muted)' }}>{a.nombreLider}</td>
      <td style={{ ...TD, textAlign: 'left', color: 'var(--z-text-muted)' }}>{a.nombreCartera}</td>
      <td style={{ ...TD, textAlign: 'center' }}>{a.gestionado}</td>
      <td style={{ ...TD, textAlign: 'right', color: C.brand, fontWeight: 600 }}>{cop(a.recaudo)}</td>
      <td style={{ ...TD, textAlign: 'right', color: C.honorarios }}>{cop(a.honorarios)}</td>
      <td style={{ ...TD, textAlign: 'right', color: 'var(--z-text-muted)' }}>
        {a.tieneMeta ? cop(a.meta) : '—'}
      </td>
      <td style={{ ...TD, textAlign: 'right', color: 'var(--z-text-muted)' }}>
        {a.tieneMeta ? cop(a.metaHonorarios) : '—'}
      </td>
      <td style={{ ...TD, textAlign: 'right', color: 'var(--z-text-muted)' }}>
        {a.tieneMeta ? cop(a.deberiaLlevarHoy) : '—'}
      </td>
      <td style={{ ...TD, textAlign: 'right', color: a.faltaParaMeta > 0 ? C.rojo : C.verde }}>
        {a.tieneMeta ? cop(a.faltaParaMeta) : '—'}
      </td>
      <td style={{ ...TD, textAlign: 'right' }}>{cop(a.promedioPagoDia)}</td>
      <td style={{ ...TD, textAlign: 'center' }}>
        <Semaforo recaudo={a.recaudo} meta={a.meta} deberiaHoy={a.deberiaLlevarHoy} />
      </td>
    </tr>
  );
}

// ╔══════════════════════════════════════════════════════════════════════════════╗
// ║  TAB — LÍDERES (agregado por líder)                                         ║
// ╚══════════════════════════════════════════════════════════════════════════════╝
interface LiderAgregado {
  liderID:          number;
  nombreLider:      string;
  carteras:         number;
  asesores:         number;
  capacidad:        number;
  meta:             number;
  metaHonorarios:   number;
  recaudo:          number;
  honorarios:       number;
  deberiaLlevarHoy: number;
  proyeccionCierre: number;
  tieneMeta:        boolean;
  cumplimiento:     number;
}

function TabLideres({ año, mes }: { año: number; mes: number }) {
  const { data, isLoading, isError } = useQuery({
    queryKey: ['inf-lideres', año, mes],
    queryFn:  () => getInformeLideres(año, mes),
  });

  type OrdenGrafico = 'honorarios' | 'proyeccion' | 'metaHonorarios';
  const [ordenGrafico, setOrdenGrafico] = useState<OrdenGrafico>('honorarios');

  if (isLoading) return <Cargando />;
  if (isError || !data) return <ErrorMsg />;

  // Agregar todas las carteras de cada líder en una sola fila
  const porLider = new Map<number, LiderAgregado>();
  for (const f of data.liderCarteras) {
    const prev = porLider.get(f.liderID);
    if (prev) {
      prev.carteras         += 1;
      prev.capacidad        += f.capacidad;
      prev.meta             += f.metaCartera;
      prev.metaHonorarios   += f.metaHonorarios;
      prev.recaudo          += f.recaudo;
      prev.honorarios       += f.honorarios;
      prev.deberiaLlevarHoy += f.deberiaLlevarHoy;
      prev.proyeccionCierre += f.proyeccionCierre;
      prev.tieneMeta         = prev.tieneMeta || f.tieneMeta;
    } else {
      porLider.set(f.liderID, {
        liderID:          f.liderID,
        nombreLider:      f.nombreLider,
        carteras:         1,
        asesores:         f.totalAsesores,
        capacidad:        f.capacidad,
        meta:             f.metaCartera,
        metaHonorarios:   f.metaHonorarios,
        recaudo:          f.recaudo,
        honorarios:       f.honorarios,
        deberiaLlevarHoy: f.deberiaLlevarHoy,
        proyeccionCierre: f.proyeccionCierre,
        tieneMeta:        f.tieneMeta,
        cumplimiento:     0,
      });
    }
  }
  const lideres: LiderAgregado[] = Array.from(porLider.values()).map(l => ({
    ...l, cumplimiento: l.meta > 0 ? l.recaudo / l.meta : 0,
  }));

  const chartData = [...lideres]
    .sort((a, b) => {
      const campo = ordenGrafico === 'proyeccion' ? 'proyeccionCierre' : ordenGrafico;
      return b[campo] - a[campo];
    })
    .map(l => ({
      label:           l.nombreLider,
      honorarios:      l.honorarios,
      deberiaHoy:      l.deberiaLlevarHoy,
      metaHonorarios:  l.metaHonorarios,
      proyeccion:      l.proyeccionCierre,
    }));

  const promDeberiaHoy = lideres.length > 0
    ? lideres.reduce((s, l) => s + l.deberiaLlevarHoy, 0) / lideres.length
    : 0;

  return (
    <div style={{ display: 'flex', flexDirection: 'column', gap: 20 }}>

      {/* KPIs */}
      <div style={{ display: 'flex', gap: 12, flexWrap: 'wrap' }}>
        <KpiCard label="Total Recaudo"   value={cop(data.totalRecaudo)} color={C.brand} />
        <KpiCard label="Total Meta"      value={cop(data.totalMeta)}    color={C.meta}  />
        <KpiCard label="Líderes Activos" value={String(data.totalLideres)} />
        <KpiCard
          label="% vs Meta Global"
          value={data.totalMeta > 0 ? `${((data.totalRecaudo / data.totalMeta) * 100).toFixed(1)}%` : '—'}
          color={data.totalRecaudo >= data.totalMeta ? C.verde : C.naranja}
        />
      </div>

      {/* Gráfico agrupado Meta | Recaudo | Proyección por líder */}
      <div style={{ background: 'var(--z-bg-card)', border: '1px solid var(--z-border)', borderRadius: 10, padding: '20px 16px' }}>
        <div style={{ display: 'flex', alignItems: 'center', justifyContent: 'space-between', marginBottom: 16 }}>
          <p style={{ fontSize: 13, fontWeight: 600, color: 'var(--z-text-primary)', margin: 0 }}>
            Honorarios · Debería hoy · Meta Honorarios por líder
          </p>
          <div style={{ display: 'flex', alignItems: 'center', gap: 6, fontSize: 12, color: 'var(--z-text-muted)' }}>
            <span>Ordenar por</span>
            {(['honorarios', 'proyeccion', 'metaHonorarios'] as OrdenGrafico[]).map(op => {
              const labels = { honorarios: 'Honorarios', proyeccion: 'Proyección', metaHonorarios: 'Meta Hon.' };
              const active = ordenGrafico === op;
              return (
                <button key={op} onClick={() => setOrdenGrafico(op)} style={{
                  padding: '3px 10px', borderRadius: 6, border: '1px solid var(--z-border)',
                  background: active ? C.naranja : 'transparent',
                  color: active ? '#fff' : 'var(--z-text-muted)',
                  cursor: 'pointer', fontSize: 12, fontWeight: active ? 600 : 400,
                }}>
                  {labels[op]}
                </button>
              );
            })}
          </div>
        </div>
        <ResponsiveContainer width="100%" height={280}>
          <BarChart data={chartData} margin={{ left: 10, right: 10, top: 4, bottom: 55 }}>
            <CartesianGrid vertical={false} stroke="var(--z-border)" />
            <XAxis dataKey="label" tick={{ fontSize: 11, fill: 'var(--z-text-muted)', angle: -25, textAnchor: 'end' }} interval={0} />
            <YAxis tickFormatter={v => cop(v)} tick={{ fontSize: 11, fill: 'var(--z-text-muted)' }} width={110} />
            <Tooltip content={<TooltipCOP />} />
            <Legend wrapperStyle={{ fontSize: 12 }} />
            <Bar dataKey="honorarios"     name="Honorarios"      fill={C.honorarios} radius={[4, 4, 0, 0]} />
            <Bar dataKey="deberiaHoy"     name="Debería hoy"     fill={C.proyeccion} radius={[4, 4, 0, 0]} />
            <Bar dataKey="metaHonorarios" name="Meta Honorarios" fill={C.meta}       radius={[4, 4, 0, 0]} />
            {promDeberiaHoy > 0 && (
              <ReferenceLine y={promDeberiaHoy} stroke={C.proyeccion} strokeDasharray="4 3" strokeWidth={1.5}
                label={{ value: `Prom. ${cop(promDeberiaHoy)}`, position: 'insideTopRight', fontSize: 11, fill: C.proyeccion }} />
            )}
          </BarChart>
        </ResponsiveContainer>
      </div>

      {/* Tabla */}
      <TablaLideres lideres={lideres} />
    </div>
  );
}

function TablaLideres({ lideres }: { lideres: LiderAgregado[] }) {
  const { sorted, sortKey, sortDir, handleSort } =
    useSortable(lideres as unknown as Record<string, unknown>[]);
  const th = (label: string, k: string, align?: 'left' | 'right' | 'center') => (
    <ThSort label={label} k={k} sortKey={sortKey as string | null} sortDir={sortDir} onSort={handleSort} align={align} />
  );

  return (
    <div style={{ background: 'var(--z-bg-card)', border: '1px solid var(--z-border)', borderRadius: 10, overflow: 'hidden' }}>
      <div style={{ overflowX: 'auto' }}>
        <table style={{ width: '100%', borderCollapse: 'collapse', fontSize: 13 }}>
          <thead>
            <tr style={{ background: 'var(--z-bg-page)', borderBottom: '1px solid var(--z-border)' }}>
              {th('Líder',             'nombreLider',     'left')}
              {th('Carteras',          'carteras',        'center')}
              {th('Asesores',          'asesores',        'center')}
              {th('Capacidad',         'capacidad',       'center')}
              {th('Recaudo',           'recaudo')}
              {th('Honorarios',        'honorarios')}
              {th('Meta Recaudo',      'meta')}
              {th('Meta Hon.',         'metaHonorarios')}
              {th('Debería hoy',       'deberiaLlevarHoy')}
              {th('Proyección cierre', 'proyeccionCierre')}
              {th('Cump.',             'cumplimiento',    'center')}
            </tr>
          </thead>
          <tbody>
            {(sorted as unknown as LiderAgregado[]).map(l => {
              const proyOk = l.proyeccionCierre >= l.meta && l.meta > 0;
              return (
                <tr key={l.liderID}>
                  <td style={{ ...TD, textAlign: 'left', fontWeight: 500, color: 'var(--z-text-primary)' }}>{l.nombreLider}</td>
                  <td style={{ ...TD, textAlign: 'center', color: 'var(--z-text-muted)' }}>{l.carteras}</td>
                  <td style={{ ...TD, textAlign: 'center' }}>{l.asesores}</td>
                  <td style={{ ...TD, textAlign: 'center' }}>{l.capacidad.toLocaleString('es-CO')}</td>
                  <td style={{ ...TD, textAlign: 'right', color: C.brand, fontWeight: 600 }}>{cop(l.recaudo)}</td>
                  <td style={{ ...TD, textAlign: 'right', color: C.honorarios }}>{cop(l.honorarios)}</td>
                  <td style={{ ...TD, textAlign: 'right', color: 'var(--z-text-muted)' }}>{l.tieneMeta ? cop(l.meta) : '—'}</td>
                  <td style={{ ...TD, textAlign: 'right', color: 'var(--z-text-muted)' }}>{l.tieneMeta ? cop(l.metaHonorarios) : '—'}</td>
                  <td style={{ ...TD, textAlign: 'right', color: 'var(--z-text-muted)' }}>{l.tieneMeta ? cop(l.deberiaLlevarHoy) : '—'}</td>
                  <td style={{ ...TD, textAlign: 'right', color: proyOk ? C.verde : C.naranja, fontWeight: 600 }}>{cop(l.proyeccionCierre)}</td>
                  <td style={{ ...TD, textAlign: 'center' }}>
                    <Semaforo recaudo={l.recaudo} meta={l.meta} deberiaHoy={l.deberiaLlevarHoy} />
                  </td>
                </tr>
              );
            })}
          </tbody>
        </table>
      </div>
    </div>
  );
}

// ╔══════════════════════════════════════════════════════════════════════════════╗
// ║  TAB — CARTERAS (mes pasado vs este mes)                                    ║
// ╚══════════════════════════════════════════════════════════════════════════════╝
function TabCarteras() {
  const [añoA, setAñoA] = useState(AÑO_ANTERIOR);
  const [mesA,  setMesA]  = useState(MES_ANTERIOR);
  const [añoB, setAñoB] = useState(AÑO_ACTUAL);
  const [mesB,  setMesB]  = useState(MES_ACTUAL);

  const { data, isLoading, isError } = useQuery({
    queryKey: ['inf-carteras', añoA, mesA, añoB, mesB],
    queryFn:  () => getInformeCarteras(añoA, mesA, añoB, mesB),
  });

  const labelA = data?.periodoA.nombre ?? `${MESES[mesA - 1]} ${añoA}`;
  const labelB = data?.periodoB.nombre ?? `${MESES[mesB - 1]} ${añoB}`;

  const chartData = (data?.carteras ?? []).map(c => ({
    nombre:   c.nombreCartera.length > 14 ? c.nombreCartera.slice(0, 14) + '…' : c.nombreCartera,
    [labelA]: c.recaudoA,
    [labelB]: c.recaudoB,
  }));

  return (
    <div style={{ display: 'flex', flexDirection: 'column', gap: 20 }}>

      {/* Selectores */}
      <div style={{ background: 'var(--z-bg-card)', border: '1px solid var(--z-border)', borderRadius: 10, padding: '14px 20px', display: 'flex', gap: 24, flexWrap: 'wrap', alignItems: 'center' }}>
        <SelectorPeriodo año={añoA} mes={mesA} onAño={setAñoA} onMes={setMesA} label="Período A:" />
        <span style={{ color: 'var(--z-text-muted)', fontSize: 18 }}>vs</span>
        <SelectorPeriodo año={añoB} mes={mesB} onAño={setAñoB} onMes={setMesB} label="Período B:" />
      </div>

      {isLoading && <Cargando />}
      {isError   && <ErrorMsg />}

      {data && (
        <>
          <div style={{ display: 'flex', gap: 12, flexWrap: 'wrap' }}>
            <KpiCard label={`Recaudo ${labelA}`} value={cop(data.totalRecaudoA)} color={C.mesA} />
            <KpiCard label={`Recaudo ${labelB}`} value={cop(data.totalRecaudoB)} color={C.mesB} />
            <KpiCard
              label="Variación"
              value={cop(Math.abs(data.variacionTotal))}
              sub={data.variacionTotal >= 0 ? '▲ Creció' : '▼ Cayó'}
              color={data.variacionTotal >= 0 ? C.verde : C.rojo}
            />
          </div>

          <div style={{ background: 'var(--z-bg-card)', border: '1px solid var(--z-border)', borderRadius: 10, padding: '20px 16px' }}>
            <p style={{ fontSize: 13, fontWeight: 600, color: 'var(--z-text-primary)', marginBottom: 16 }}>
              Recaudo por cartera — {labelA} vs {labelB}
            </p>
            <ResponsiveContainer width="100%" height={Math.max(280, chartData.length * 60)}>
              <BarChart data={chartData} margin={{ left: 10, right: 10, top: 4, bottom: 50 }}>
                <CartesianGrid vertical={false} stroke="var(--z-border)" />
                <XAxis dataKey="nombre" tick={{ fontSize: 11, fill: 'var(--z-text-muted)' }} angle={-30} textAnchor="end" interval={0} />
                <YAxis tickFormatter={v => cop(v)} tick={{ fontSize: 11, fill: 'var(--z-text-muted)' }} width={110} />
                <Tooltip content={<TooltipCOP />} />
                <Legend wrapperStyle={{ fontSize: 12 }} />
                <Bar dataKey={labelA} fill={C.mesA} radius={[4, 4, 0, 0]} />
                <Bar dataKey={labelB} fill={C.mesB} radius={[4, 4, 0, 0]} />
              </BarChart>
            </ResponsiveContainer>
          </div>

          <TablaComparativa carteras={data.carteras} labelA={labelA} labelB={labelB} />
        </>
      )}
    </div>
  );
}

// ╔══════════════════════════════════════════════════════════════════════════════╗
// ║  TAB — COMPARATIVO LIBRE                                                    ║
// ╚══════════════════════════════════════════════════════════════════════════════╝
function TabComparativo() {
  const [añoA, setAñoA] = useState(AÑO_ACTUAL);
  const [mesA,  setMesA]  = useState(MES_ANTERIOR);
  const [añoB, setAñoB] = useState(AÑO_ACTUAL);
  const [mesB,  setMesB]  = useState(MES_ACTUAL);

  const { data, isLoading, isError } = useQuery({
    queryKey: ['inf-comparativo', añoA, mesA, añoB, mesB],
    queryFn:  () => getInformeCarteras(añoA, mesA, añoB, mesB),
  });

  const labelA = data?.periodoA.nombre ?? `${MESES[mesA - 1]} ${añoA}`;
  const labelB = data?.periodoB.nombre ?? `${MESES[mesB - 1]} ${añoB}`;

  const chartData = (data?.carteras ?? []).map(c => ({
    nombre:   c.nombreCartera.length > 14 ? c.nombreCartera.slice(0, 14) + '…' : c.nombreCartera,
    [labelA]: c.recaudoA,
    [labelB]: c.recaudoB,
  }));

  const variPct = data && data.totalRecaudoA > 0
    ? ((data.totalRecaudoB - data.totalRecaudoA) / data.totalRecaudoA * 100).toFixed(1)
    : null;

  return (
    <div style={{ display: 'flex', flexDirection: 'column', gap: 20 }}>

      <div style={{ background: 'var(--z-bg-card)', border: '1px solid var(--z-border)', borderRadius: 10, padding: '14px 20px', display: 'flex', gap: 24, flexWrap: 'wrap', alignItems: 'center' }}>
        <SelectorPeriodo año={añoA} mes={mesA} onAño={setAñoA} onMes={setMesA} label="Mes A:" />
        <span style={{ color: 'var(--z-text-muted)', fontSize: 18 }}>vs</span>
        <SelectorPeriodo año={añoB} mes={mesB} onAño={setAñoB} onMes={setMesB} label="Mes B:" />
      </div>

      {isLoading && <Cargando />}
      {isError   && <ErrorMsg />}

      {data && (
        <>
          <div style={{ display: 'flex', gap: 12, flexWrap: 'wrap' }}>
            <KpiCard label={`Recaudo ${labelA}`}   value={cop(data.totalRecaudoA)} color={C.mesA} />
            <KpiCard label={`Recaudo ${labelB}`}   value={cop(data.totalRecaudoB)} color={C.mesB} />
            <KpiCard
              label="Variación total"
              value={pct(variPct !== null ? Number(variPct) : null)}
              color={data.variacionTotal >= 0 ? C.verde : C.rojo}
            />
          </div>

          <div style={{ background: 'var(--z-bg-card)', border: '1px solid var(--z-border)', borderRadius: 10, padding: '20px 16px' }}>
            <p style={{ fontSize: 13, fontWeight: 600, color: 'var(--z-text-primary)', marginBottom: 16 }}>
              {labelA} vs {labelB} — por cartera
            </p>
            <ResponsiveContainer width="100%" height={Math.max(280, chartData.length * 60)}>
              <BarChart data={chartData} margin={{ left: 10, right: 10, top: 4, bottom: 50 }}>
                <CartesianGrid vertical={false} stroke="var(--z-border)" />
                <XAxis dataKey="nombre" tick={{ fontSize: 11, fill: 'var(--z-text-muted)' }} angle={-30} textAnchor="end" interval={0} />
                <YAxis tickFormatter={v => cop(v)} tick={{ fontSize: 11, fill: 'var(--z-text-muted)' }} width={110} />
                <Tooltip content={<TooltipCOP />} />
                <Legend wrapperStyle={{ fontSize: 12 }} />
                <ReferenceLine y={0} stroke="var(--z-border)" />
                <Bar dataKey={labelA} fill={C.mesA} radius={[4, 4, 0, 0]} />
                <Bar dataKey={labelB} fill={C.mesB} radius={[4, 4, 0, 0]} />
              </BarChart>
            </ResponsiveContainer>
          </div>

          <TablaComparativa carteras={data.carteras} labelA={labelA} labelB={labelB} />
        </>
      )}
    </div>
  );
}

// ── Tabla compartida Carteras/Comparativo ─────────────────────────────────────
function TablaComparativa({ carteras, labelA, labelB }: {
  carteras: InformeCarteraComparativo[]; labelA: string; labelB: string;
}) {
  const { sorted, sortKey, sortDir, handleSort } =
    useSortable(carteras as unknown as Record<string, unknown>[]);
  const th = (label: string, k: string, align?: 'left' | 'right' | 'center') => (
    <ThSort label={label} k={k} sortKey={sortKey as string | null} sortDir={sortDir} onSort={handleSort} align={align} />
  );

  return (
    <div style={{ background: 'var(--z-bg-card)', border: '1px solid var(--z-border)', borderRadius: 10, overflow: 'hidden' }}>
      <div style={{ overflowX: 'auto' }}>
        <table style={{ width: '100%', borderCollapse: 'collapse', fontSize: 13 }}>
          <thead>
            <tr style={{ background: 'var(--z-bg-page)', borderBottom: '1px solid var(--z-border)' }}>
              {th('Cartera',            'nombreCartera',      'left')}
              {th(`Recaudo ${labelA}`,  'recaudoA')}
              {th(`Hon. ${labelA}`,     'honorariosA')}
              {th(`Recaudo ${labelB}`,  'recaudoB')}
              {th(`Hon. ${labelB}`,     'honorariosB')}
              {th('Variación',          'variacionRecaudo')}
              {th('%',                  'porcentajeVariacion', 'center')}
            </tr>
          </thead>
          <tbody>
            {(sorted as unknown as InformeCarteraComparativo[]).map(c => (
              <tr key={c.carteraID}>
                <td style={{ ...TD, textAlign: 'left', fontWeight: 500, color: 'var(--z-text-primary)' }}>{c.nombreCartera}</td>
                <td style={{ ...TD, textAlign: 'right', color: 'var(--z-text-muted)' }}>{cop(c.recaudoA)}</td>
                <td style={{ ...TD, textAlign: 'right', color: 'var(--z-text-muted)' }}>{cop(c.honorariosA)}</td>
                <td style={{ ...TD, textAlign: 'right', color: C.brand, fontWeight: 600 }}>{cop(c.recaudoB)}</td>
                <td style={{ ...TD, textAlign: 'right', color: C.honorarios }}>{cop(c.honorariosB)}</td>
                <td style={{ ...TD, textAlign: 'right', color: c.variacionRecaudo >= 0 ? C.verde : C.rojo, fontWeight: 600 }}>
                  {c.variacionRecaudo >= 0 ? '+' : ''}{cop(c.variacionRecaudo)}
                </td>
                <td style={{ ...TD, textAlign: 'center', color: c.variacionRecaudo >= 0 ? C.verde : C.rojo }}>
                  {pct(c.porcentajeVariacion)}
                </td>
              </tr>
            ))}
          </tbody>
        </table>
      </div>
    </div>
  );
}

// ╔══════════════════════════════════════════════════════════════════════════════╗
// ║  PÁGINA PRINCIPAL                                                           ║
// ╚══════════════════════════════════════════════════════════════════════════════╝
type Tab = 'agentes' | 'lideres' | 'carteras' | 'comparativo';

const TABS: { id: Tab; label: string }[] = [
  { id: 'agentes',     label: 'Agentes'     },
  { id: 'lideres',     label: 'Líderes'     },
  { id: 'carteras',    label: 'Carteras'    },
  { id: 'comparativo', label: 'Comparativo' },
];

export function InformesRecaudoPage() {
  const [tabActiva, setTabActiva] = useState<Tab>('agentes');
  const [año, setAño] = useState(AÑO_ACTUAL);
  const [mes, setMes] = useState(MES_ACTUAL);

  const usaPeriodoGlobal = tabActiva === 'agentes' || tabActiva === 'lideres';

  return (
    <div style={{ display: 'flex', flexDirection: 'column', gap: 20, maxWidth: 1300, margin: '0 auto' }}>

      {/* Encabezado */}
      <div style={{ display: 'flex', justifyContent: 'space-between', alignItems: 'flex-start', flexWrap: 'wrap', gap: 12 }}>
        <div>
          <h1 style={{ fontSize: 20, fontWeight: 600, color: 'var(--z-text-primary)', margin: 0 }}>
            Informes de Recaudo
          </h1>
          <p style={{ fontSize: 13, color: 'var(--z-text-muted)', margin: '4px 0 0' }}>
            Seguimiento de recaudo, honorarios y cumplimiento de metas
          </p>
        </div>

        {usaPeriodoGlobal && (
          <SelectorPeriodo año={año} mes={mes} onAño={setAño} onMes={setMes} />
        )}
      </div>

      {/* Tabs */}
      <div style={{ display: 'flex', gap: 2, borderBottom: '1px solid var(--z-border)' }}>
        {TABS.map(t => (
          <button
            key={t.id}
            onClick={() => setTabActiva(t.id)}
            style={{
              padding: '9px 20px',
              fontSize: 13,
              fontWeight: tabActiva === t.id ? 600 : 400,
              color: tabActiva === t.id ? 'var(--z-brand)' : 'var(--z-text-muted)',
              background: 'none',
              border: 'none',
              borderBottom: `2px solid ${tabActiva === t.id ? 'var(--z-brand)' : 'transparent'}`,
              cursor: 'pointer',
              marginBottom: -1,
              transition: 'color 0.15s',
            }}
          >
            {t.label}
          </button>
        ))}
      </div>

      {/* Contenido */}
      {tabActiva === 'agentes'     && <TabAgentes     año={año} mes={mes} />}
      {tabActiva === 'lideres'     && <TabLideres     año={año} mes={mes} />}
      {tabActiva === 'carteras'    && <TabComparativo />}
      {tabActiva === 'comparativo' && <TabCarteras />}
    </div>
  );
}
