import { useState, useMemo, useRef, useCallback, useEffect } from 'react';
import { useAuth } from '../../context/AuthContext';
import { useQuery } from '@tanstack/react-query';
import {
  ResponsiveContainer,
  BarChart,
  Bar,
  XAxis,
  YAxis,
  CartesianGrid,
  Tooltip,
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
  mesC:       '#10b981',
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
const MES_ANTERIOR  = MES_ACTUAL === 1 ? 12 : MES_ACTUAL - 1;
const AÑO_ANTERIOR  = MES_ACTUAL === 1 ? AÑO_ACTUAL - 1 : AÑO_ACTUAL;
const MES_DOS_ANTES = MES_ACTUAL <= 2  ? MES_ACTUAL - 2 + 12 : MES_ACTUAL - 2;
const AÑO_DOS_ANTES = MES_ACTUAL <= 2  ? AÑO_ACTUAL - 1 : AÑO_ACTUAL;

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
function KpiCard({ label, value, valueSuffix, sub, color }: {
  label: string; value: string; valueSuffix?: string; sub?: string; color?: string;
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
        {valueSuffix && (
          <span style={{ fontSize: 16, fontWeight: 400, color: '#94a3b8', marginLeft: 4 }}>
            {valueSuffix}
          </span>
        )}
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
// Colorimetría: ≥100% verde · ≥90% amarillo · <90% rojo (basado en % vs meta total)
function Semaforo({ valorActual, valorMeta }: {
  valorActual: number; valorMeta: number;
}) {
  if (valorMeta === 0) return <span style={{ color: 'var(--z-text-muted)', fontSize: 12 }}>—</span>;
  const pctCump = (valorActual / valorMeta) * 100;
  const color   = pctCump >= 100 ? '#16a34a' : pctCump >= 90 ? '#ca8a04' : '#dc2626';
  return (
    <span style={{ color, fontWeight: 600, fontSize: 13 }}>{pctCump.toFixed(1)}%</span>
  );
}

function ChipTipoMeta({ tipo }: { tipo: 'Recaudo' | 'Honorarios' | null }) {
  if (!tipo) return <span style={{ color: 'var(--z-text-muted)', fontSize: 11 }}>—</span>;
  const color = tipo === 'Honorarios' ? C.honorarios : C.brand;
  return (
    <span style={{
      fontSize: 10, fontWeight: 700, padding: '2px 6px', borderRadius: 4,
      background: `${color}22`, color, whiteSpace: 'nowrap',
    }}>
      {tipo}
    </span>
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

// ── Descarga PDF ──────────────────────────────────────────────────────────────
type DownloadRegistrar = (fn: () => Promise<void>) => void;

// Estructura de datos para la tabla del PDF (renderizada con jspdf-autotable,
// sin capturas DOM — garantiza alineación perfecta independiente de CSS).
interface PdfTableData {
  head:         string[][];
  body:         (string | number | null)[][];
  // Índice de la columna "Cump." que contiene el ratio crudo (0..1), -1 si no existe.
  // didParseCell la formatea como % y aplica colorimetría verde/amarillo/rojo.
  cumpColIndex: number;
  colStyles?:   Record<number, { halign: 'left' | 'center' | 'right' }>;
}

async function buildExportarPDF(opts: {
  tituloArchivo: string;
  tituloPDF:     string;
  subtitulo?:    string;
  pdfTable:      PdfTableData;
  refGrafico:    React.RefObject<HTMLDivElement | null>;
}): Promise<void> {
  const [{ default: jsPDF }, { default: html2canvas }, { autoTable }] = await Promise.all([
    import('jspdf'),
    import('html2canvas'),
    import('jspdf-autotable'),  // v5: exporta autoTable como función standalone
  ]);

  const { tituloArchivo, tituloPDF, subtitulo, pdfTable, refGrafico } = opts;
  const PW = 297; const PH = 210; const M = 10;
  const HEADER_H = 28;
  const FOOTER_H = 10;
  const TOP      = HEADER_H + 5;
  const MAX_CONT = PH - TOP - FOOTER_H;

  const renderHeader = (pdf: InstanceType<typeof jsPDF>, pagina: number) => {
    pdf.setFillColor(37, 99, 235);
    pdf.rect(0, 0, PW, HEADER_H, 'F');
    pdf.setTextColor(255, 255, 255);
    pdf.setFont('helvetica', 'bold');
    pdf.setFontSize(18);
    pdf.text(tituloPDF, M, 13);
    if (subtitulo) {
      pdf.setFont('helvetica', 'normal');
      pdf.setFontSize(9);
      pdf.setTextColor(186, 210, 255);
      pdf.text(subtitulo, M, 21);
    }
    pdf.setDrawColor(203, 213, 225); pdf.setLineWidth(0.3);
    pdf.line(M, PH - FOOTER_H + 2, PW - M, PH - FOOTER_H + 2);
    pdf.setFont('helvetica', 'normal'); pdf.setFontSize(8); pdf.setTextColor(100, 116, 139);
    pdf.text('Zentinela · Sistema de Gestión de Recaudo', M, PH - 4);
    pdf.text(`Página ${pagina}`, PW - M, PH - 4, { align: 'right' });
  };

  const pdf = new jsPDF({ orientation: 'landscape', unit: 'mm', format: 'a4' });

  // ── Página 1: Tabla con jspdf-autotable (sin captura DOM, texto perfectamente centrado) ──
  renderHeader(pdf, 1);
  autoTable(pdf, {
    startY: TOP,
    head:   pdfTable.head,
    body:   pdfTable.body,
    styles: {
      fontSize:    8,
      cellPadding: 2.5,
      valign:      'middle',
      halign:      'right',
      overflow:    'ellipsize',
    },
    headStyles: {
      fillColor: [37, 99, 235],
      textColor: 255,
      fontStyle: 'bold',
      halign:    'center',
      fontSize:  8,
    },
    alternateRowStyles: { fillColor: [248, 250, 252] },
    columnStyles:       pdfTable.colStyles ?? {},
    // Colorimetría de la columna Cump.: ≥100% verde · ≥90% amarillo · <90% rojo
    didParseCell: (data: any) => {
      if (pdfTable.cumpColIndex < 0) return;
      if (data.section !== 'body' || data.column.index !== pdfTable.cumpColIndex) return;
      const val = Number(data.cell.raw);
      if (isNaN(val) || val < 0) {
        data.cell.text              = ['—'];
        data.cell.styles.textColor  = [148, 163, 184];
        data.cell.styles.halign     = 'center';
      } else {
        const pctVal = val * 100;
        data.cell.text = [`${pctVal.toFixed(1)}%`];
        const color: [number, number, number] =
          pctVal >= 100 ? [22, 163, 74] : pctVal >= 90 ? [202, 138, 4] : [220, 38, 38];
        data.cell.styles.textColor = color;
        data.cell.styles.fontStyle = 'bold';
        data.cell.styles.halign    = 'center';
      }
    },
    margin: { left: M, right: M, top: TOP, bottom: FOOTER_H + 4 },
  });

  // ── Página 2: Gráfico con html2canvas ────────────────────────────────────────
  pdf.addPage();
  renderHeader(pdf, 2);

  if (refGrafico.current) {
    // Resolver CSS vars leyendo estilos computados antes del clone
    const origEls = [refGrafico.current, ...Array.from(refGrafico.current.querySelectorAll<HTMLElement>('*'))];
    const snaps   = origEls.map(el => {
      const cs = getComputedStyle(el);
      return {
        color: cs.color, bg: cs.backgroundColor,
        btc: cs.borderTopColor, brc: cs.borderRightColor,
        bbc: cs.borderBottomColor, blc: cs.borderLeftColor,
      };
    });
    const c = await html2canvas(refGrafico.current, {
      scale: 2, backgroundColor: '#ffffff', logging: false,
      onclone: (_doc, cloned) => {
        const clonedEls = [cloned, ...Array.from(cloned.querySelectorAll<HTMLElement>('*'))];
        clonedEls.forEach((el, i) => {
          const s = snaps[i]; if (!s) return;
          el.style.color             = s.color;
          el.style.backgroundColor   = s.bg;
          el.style.borderTopColor    = s.btc;
          el.style.borderRightColor  = s.brc;
          el.style.borderBottomColor = s.bbc;
          el.style.borderLeftColor   = s.blc;
        });
      },
    });
    const INNER_W = PW - M * 2;
    const ratio   = (c.height / 2) / (c.width / 2);
    const imgW    = INNER_W;
    const imgH    = Math.min(imgW * ratio, MAX_CONT);
    // Centrar vertical y horizontalmente dentro del área disponible
    const x = (PW - imgW) / 2;
    const y = TOP + Math.max(0, (MAX_CONT - imgH) / 2);
    pdf.addImage(c.toDataURL('image/png'), 'PNG', x, y, imgW, imgH);
  }

  pdf.save(`${tituloArchivo}.pdf`);
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
function LeyendaGrafico({ items }: { items: { color: string; label: string }[] }) {
  return (
    <div style={{ display: 'flex', alignItems: 'center', gap: 14, flexWrap: 'wrap' }}>
      {items.map(it => (
        <div key={it.label} style={{ display: 'flex', alignItems: 'center', gap: 5 }}>
          <span style={{ width: 10, height: 10, borderRadius: 2, background: it.color, flexShrink: 0, display: 'inline-block' }} />
          <span style={{ fontSize: 12, color: 'var(--z-text-muted)' }}>{it.label}</span>
        </div>
      ))}
    </div>
  );
}

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
function TabAgentes({ año, mes, onRegisterDownload }: {
  año: number; mes: number; onRegisterDownload?: DownloadRegistrar;
}) {
  const { usuario } = useAuth();
  const esLider = usuario?.rol === 'Líder';
  const [filtroLider,   setFiltroLider]   = useState(() => esLider ? String(usuario?.liderID ?? '') : '');
  const [filtroCartera, setFiltroCartera] = useState('');
  const [busqueda,      setBusqueda]      = useState('');
  const refGrafico = useRef<HTMLDivElement>(null);
  // Ref siempre actualizado: la fn de descarga lo lee en el momento del clic (datos frescos)
  const latestRef  = useRef<{ agentesVisibles: InformeAgenteMes[]; esLider: boolean }>({ agentesVisibles: [], esLider: false });

  const { data, isLoading, isError } = useQuery({
    queryKey: ['inf-agentes', año, mes],
    queryFn:  () => getInformeAgentes(año, mes),
  });

  // Filtrado cliente-lado
  const agentesVisibles = useMemo(() => {
    if (!data) return [];
    const termino = busqueda.trim().toLowerCase();
    return data.agentes.filter(a => {
      if (filtroLider   && String(a.liderID) !== filtroLider)   return false;
      if (filtroCartera && a.nombreCartera   !== filtroCartera) return false;
      if (termino       && !a.nombreAgente.toLowerCase().includes(termino)) return false;
      return true;
    });
  }, [data, filtroLider, filtroCartera, busqueda]);

  // Actualizar ref en cada render para que la descarga siempre use el filtro actual
  latestRef.current = { agentesVisibles, esLider };

  useEffect(() => {
    if (!onRegisterDownload) return;
    const nombreMes = MESES[mes - 1];
    onRegisterDownload(() => {
      const { agentesVisibles, esLider } = latestRef.current;
      if (agentesVisibles.length === 0) return Promise.resolve();

      const headCols = ['#', 'Agente', ...(esLider ? [] : ['Líder']), 'Cartera',
        'Gestionado', 'Recaudo', 'Honorarios', 'Tipo Meta',
        'Meta', 'Debería hoy', 'Falta', 'Prom./día', 'Cump.'];
      const cumpColIndex = headCols.length - 1;
      const body = agentesVisibles.map((a, i) => {
        const valorActual = a.tipoMeta === 'Honorarios' ? a.honorarios : a.recaudo;
        return [
          i + 1, a.nombreAgente,
          ...(esLider ? [] : [a.nombreLider]),
          a.nombreCartera, a.gestionado,
          cop(a.recaudo), cop(a.honorarios),
          a.tipoMeta ?? '—',
          a.tieneMeta ? cop(a.valorMeta) : '—',
          a.tieneMeta ? cop(a.deberiaLlevarHoy) : '—',
          a.tieneMeta ? cop(a.faltaParaMeta) : '—',
          cop(a.promedioPagoDia),
          a.valorMeta > 0 ? valorActual / a.valorMeta : -1,
        ];
      });
      const offset = esLider ? 0 : 1;
      const colStyles: Record<number, { halign: 'left' | 'center' | 'right' }> = {
        0: { halign: 'center' },
        1: { halign: 'left' },
        ...(esLider ? {} : { 2: { halign: 'left' } }),
        [2 + offset]: { halign: 'left' },
        [3 + offset]: { halign: 'center' },
        [6 + offset]: { halign: 'center' },
      };
      return buildExportarPDF({
        tituloArchivo: `Informe_Agentes_${nombreMes}_${año}`,
        tituloPDF:     `Informe Agentes — ${nombreMes} ${año}`,
        subtitulo:     `Generado el ${new Date().toLocaleDateString('es-CO', { day: 'numeric', month: 'long', year: 'numeric' })}`,
        pdfTable: { head: [headCols], body, cumpColIndex, colStyles },
        refGrafico,
      });
    });
  }, [onRegisterDownload, año, mes]);

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

  const chartData = agentesVisibles.map(a => ({
    nombre:           a.nombreAgente,
    honorarios:       a.honorarios,
    deberiaLlevarHoy: a.deberiaLlevarHoy,
    valorMeta:        a.valorMeta,
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
        <KpiCard label="Agentes con pagos"    value={String(agentesVisibles.length)} valueSuffix={data.totalAgentesActivos ? `/ ${data.totalAgentesActivos}` : undefined} />
        <KpiCard label="Pagos Verificados"  value={totalGestionado.toLocaleString('es-CO')} />
      </div>

      {/* Gráfico horizontal — menor a mayor */}
      <div style={{ background: 'var(--z-bg-card)', border: '1px solid var(--z-border)', borderRadius: 10, padding: '20px 16px' }}>

        {/* Cabecera gráfico + filtros */}
        <div style={{ display: 'flex', justifyContent: 'space-between', alignItems: 'center', flexWrap: 'wrap', gap: 12, marginBottom: 16 }}>
          <div style={{ display: 'flex', alignItems: 'center', gap: 16, flexWrap: 'wrap' }}>
            <p style={{ fontSize: 13, fontWeight: 600, color: 'var(--z-text-primary)', margin: 0 }}>
              Honorarios por asesor — menor a mayor
            </p>
            <LeyendaGrafico items={[
              { color: C.honorarios, label: 'Honorarios' },
              { color: C.proyeccion, label: 'Debería hoy' },
              { color: C.meta,       label: 'Meta' },
            ]} />
          </div>
          <div style={{ display: 'flex', gap: 10, flexWrap: 'wrap', alignItems: 'center' }}>
            {!esLider && (
              <FiltroSelect
                label="Líder:"
                value={filtroLider}
                onChange={v => { setFiltroLider(v); setFiltroCartera(''); }}
                options={optsLider}
                placeholder="Todos los líderes"
              />
            )}
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
            <div ref={refGrafico}>
            <ResponsiveContainer width="100%" height={280}>
              <BarChart data={chartData} margin={{ left: 10, right: 10, top: 4, bottom: 70 }}>
                <CartesianGrid vertical={false} stroke="var(--z-border)" />
                <XAxis dataKey="nombre" tick={{ fontSize: 11, fill: 'var(--z-text-muted)', angle: -35, textAnchor: 'end' }} interval={0} />
                <YAxis tickFormatter={v => cop(v)} tick={{ fontSize: 11, fill: 'var(--z-text-muted)' }} width={110} />
                <Tooltip content={<TooltipCOP />} />
                <Bar dataKey="honorarios"       name="Honorarios"  fill={C.honorarios} radius={[4, 4, 0, 0]} />
                <Bar dataKey="deberiaLlevarHoy" name="Debería hoy" fill={C.proyeccion} radius={[4, 4, 0, 0]} />
                <Bar dataKey="valorMeta"         name="Meta"        fill={C.meta}       radius={[4, 4, 0, 0]} />
              </BarChart>
            </ResponsiveContainer>
            </div>
          )
        }
      </div>

      {/* Tabla */}
      <TablaAgentes agentes={agentesVisibles} esLider={esLider} />
    </div>
  );
}

function TablaAgentes({ agentes, esLider }: { agentes: InformeAgenteMes[]; esLider: boolean }) {
  type Row = InformeAgenteMes & { cumplimiento: number };
  const rows: Row[] = agentes.map(a => {
    const valorActual = a.tipoMeta === 'Honorarios' ? a.honorarios : a.recaudo;
    return { ...a, cumplimiento: a.valorMeta > 0 ? valorActual / a.valorMeta : 0 };
  }) as unknown as Row[];

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
              {!esLider && th('Líder',  'nombreLider', 'left')}
              {th('Cartera',      'nombreCartera',   'left')}
              {th('Gestionado',  'gestionado',      'center')}
              {th('Recaudo',     'recaudo')}
              {th('Honorarios',  'honorarios')}
              <th style={{ ...TH, textAlign: 'center' }}>Tipo Meta</th>
              {th('Meta',        'valorMeta')}
              {th('Debería hoy', 'deberiaLlevarHoy')}
              {th('Falta',       'faltaParaMeta')}
              {th('Prom./día',   'promedioPagoDia')}
              {th('Cump.',       'cumplimiento',    'center')}
            </tr>
          </thead>
          <tbody>
            {(sorted as unknown as Row[]).map((a, i) => <FilaAgente key={a.agenteID} a={a} pos={i + 1} esLider={esLider} />)}
          </tbody>
        </table>
      </div>
    </div>
  );
}

function FilaAgente({ a, pos, esLider }: { a: InformeAgenteMes; pos: number; esLider: boolean }) {
  return (
    <tr>
      <td style={{ ...TD, textAlign: 'center', color: 'var(--z-text-muted)' }}>{pos}</td>
      <td style={{ ...TD, textAlign: 'left', fontWeight: 500, color: 'var(--z-text-primary)' }}>{a.nombreAgente}</td>
      {!esLider && <td style={{ ...TD, textAlign: 'left', color: 'var(--z-text-muted)' }}>{a.nombreLider}</td>}
      <td style={{ ...TD, textAlign: 'left', color: 'var(--z-text-muted)' }}>{a.nombreCartera}</td>
      <td style={{ ...TD, textAlign: 'center' }}>{a.gestionado}</td>
      <td style={{ ...TD, textAlign: 'right', color: C.brand, fontWeight: 600 }}>{cop(a.recaudo)}</td>
      <td style={{ ...TD, textAlign: 'right', color: C.honorarios }}>{cop(a.honorarios)}</td>
      <td style={{ ...TD, textAlign: 'center' }}>
        <ChipTipoMeta tipo={a.tipoMeta} />
      </td>
      <td style={{ ...TD, textAlign: 'right', color: 'var(--z-text-muted)' }}>
        {a.tieneMeta ? cop(a.valorMeta) : '—'}
      </td>
      <td style={{ ...TD, textAlign: 'right', color: 'var(--z-text-muted)' }}>
        {a.tieneMeta ? cop(a.deberiaLlevarHoy) : '—'}
      </td>
      <td style={{ ...TD, textAlign: 'right', color: a.faltaParaMeta > 0 ? C.rojo : C.verde }}>
        {a.tieneMeta ? cop(a.faltaParaMeta) : '—'}
      </td>
      <td style={{ ...TD, textAlign: 'right' }}>{cop(a.promedioPagoDia)}</td>
      <td style={{ ...TD, textAlign: 'center' }}>
        <Semaforo
          valorActual={a.tipoMeta === 'Honorarios' ? a.honorarios : a.recaudo}
          valorMeta={a.valorMeta}
        />
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
  valorMeta:        number;
  tipoMeta:         'Recaudo' | 'Honorarios' | null;
  recaudo:          number;
  honorarios:       number;
  deberiaLlevarHoy: number;
  proyeccionCierre: number;
  tieneMeta:        boolean;
  cumplimiento:     number;
}

function TabLideres({ año, mes, onRegisterDownload }: {
  año: number; mes: number; onRegisterDownload?: DownloadRegistrar;
}) {
  const refGrafico = useRef<HTMLDivElement>(null);
  const latestRef  = useRef<{ lideres: LiderAgregado[] }>({ lideres: [] });

  const { data, isLoading, isError } = useQuery({
    queryKey: ['inf-lideres', año, mes],
    queryFn:  () => getInformeLideres(año, mes),
  });

  const [metrica,  setMetrica]  = useState<'honorarios' | 'recaudo'>('honorarios');
  const [criterio, setCriterio] = useState<'valor' | 'proyeccion' | 'meta'>('valor');

  // Agregar todas las carteras de cada líder
  const lideres = useMemo<LiderAgregado[]>(() => {
    if (!data) return [];
    const porLider = new Map<number, LiderAgregado>();
    for (const f of data.liderCarteras) {
      const prev = porLider.get(f.liderID);
      if (prev) {
        prev.carteras         += 1;
        prev.capacidad        += f.capacidad;
        prev.valorMeta        += f.valorMeta;
        if (!prev.tipoMeta && f.tipoMeta) prev.tipoMeta = f.tipoMeta;
        prev.recaudo          += f.recaudo;
        prev.honorarios       += f.honorarios;
        prev.deberiaLlevarHoy += f.deberiaLlevarHoy;
        prev.proyeccionCierre += f.proyeccionCierre;
        prev.tieneMeta         = prev.tieneMeta || f.tieneMeta;
      } else {
        porLider.set(f.liderID, {
          liderID: f.liderID, nombreLider: f.nombreLider,
          carteras: 1, asesores: f.totalAsesores,
          capacidad: f.capacidad, valorMeta: f.valorMeta, tipoMeta: f.tipoMeta,
          recaudo: f.recaudo, honorarios: f.honorarios,
          deberiaLlevarHoy: f.deberiaLlevarHoy, proyeccionCierre: f.proyeccionCierre,
          tieneMeta: f.tieneMeta, cumplimiento: 0,
        });
      }
    }
    return Array.from(porLider.values()).map(l => ({
      ...l, cumplimiento: l.valorMeta > 0 ? l.honorarios / l.valorMeta : 0,
    }));
  }, [data]);

  latestRef.current = { lideres };

  useEffect(() => {
    if (!onRegisterDownload) return;
    const nombreMes = MESES[mes - 1];
    onRegisterDownload(() => {
      const { lideres } = latestRef.current;
      if (lideres.length === 0) return Promise.resolve();

      const headCols = ['Líder', 'Carteras', 'Asesores', 'Asignaciones',
        'Recaudo', 'Honorarios', 'Tipo Meta', 'Meta', 'Debería hoy', 'Proyección', 'Cump.'];
      const cumpColIndex = headCols.length - 1;
      const body = lideres.map(l => [
        l.nombreLider, l.carteras, l.asesores,
        l.capacidad.toLocaleString('es-CO'),
        cop(l.recaudo), cop(l.honorarios),
        l.tipoMeta ?? '—',
        l.tieneMeta ? cop(l.valorMeta) : '—',
        l.tieneMeta ? cop(l.deberiaLlevarHoy) : '—',
        cop(l.proyeccionCierre),
        l.valorMeta > 0 ? l.honorarios / l.valorMeta : -1,
      ]);
      const colStyles: Record<number, { halign: 'left' | 'center' | 'right' }> = {
        0: { halign: 'left' },
        1: { halign: 'center' }, 2: { halign: 'center' },
        3: { halign: 'center' }, 6: { halign: 'center' },
      };
      return buildExportarPDF({
        tituloArchivo: `Informe_Lideres_${nombreMes}_${año}`,
        tituloPDF:     `Informe Líderes — ${nombreMes} ${año}`,
        subtitulo:     `Generado el ${new Date().toLocaleDateString('es-CO', { day: 'numeric', month: 'long', year: 'numeric' })}`,
        pdfTable: { head: [headCols], body, cumpColIndex, colStyles },
        refGrafico,
      });
    });
  }, [onRegisterDownload, año, mes]);

  if (isLoading) return <Cargando />;
  if (isError || !data) return <ErrorMsg />;

  const lideresConMeta = lideres.filter(l => l.tieneMeta);
  const avgCumplimiento = lideresConMeta.length > 0
    ? lideresConMeta.reduce((s, l) => s + l.cumplimiento, 0) / lideresConMeta.length
    : null;

  const campoOrdenL = criterio === 'valor' ? metrica : criterio === 'proyeccion' ? 'proyeccionCierre' : 'valorMeta';

  const chartData = [...lideres]
    .sort((a, b) => (b[campoOrdenL as keyof LiderAgregado] as number) - (a[campoOrdenL as keyof LiderAgregado] as number))
    .map(l => ({
      label:        l.nombreLider,
      metricaValor: metrica === 'honorarios' ? l.honorarios : l.recaudo,
      deberiaHoy:   l.deberiaLlevarHoy,
      valorMeta:    l.valorMeta,
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
          value={avgCumplimiento !== null ? `${(avgCumplimiento * 100).toFixed(1)}%` : '—'}
          color={avgCumplimiento !== null && avgCumplimiento >= 1 ? C.verde : C.naranja}
        />
      </div>

      {/* Gráfico agrupado por líder */}
      <div style={{ background: 'var(--z-bg-card)', border: '1px solid var(--z-border)', borderRadius: 10, padding: '20px 16px' }}>
        <div style={{ display: 'flex', alignItems: 'center', justifyContent: 'space-between', marginBottom: 16, flexWrap: 'wrap', gap: 12 }}>
          <p style={{ fontSize: 13, fontWeight: 600, color: 'var(--z-text-primary)', margin: 0 }}>
            {metrica === 'honorarios' ? 'Honorarios' : 'Recaudo'} · Debería hoy · Meta por líder
          </p>
          <div style={{ display: 'flex', alignItems: 'center', gap: 12, flexWrap: 'wrap' }}>
            <LeyendaGrafico items={[
              { color: metrica === 'honorarios' ? C.honorarios : C.brand, label: metrica === 'honorarios' ? 'Honorarios' : 'Recaudo' },
              { color: C.proyeccion, label: 'Debería hoy' },
              { color: C.meta,       label: 'Meta' },
            ]} />
            <div style={{ display: 'flex', alignItems: 'center', gap: 6, fontSize: 12, flexWrap: 'wrap' }}>
              <span style={{ fontWeight: 600, color: 'var(--z-text-muted)' }}>Ordenar por</span>
              {(['honorarios', 'recaudo'] as const).map(m => (
                <button key={m} onClick={() => setMetrica(m)} style={{
                  padding: '3px 10px', borderRadius: 6, border: '1px solid var(--z-border)',
                  background: metrica === m ? C.naranja : 'transparent',
                  color: metrica === m ? '#fff' : 'var(--z-text-muted)',
                  cursor: 'pointer', fontSize: 12, fontWeight: metrica === m ? 600 : 400,
                }}>
                  {m === 'honorarios' ? 'Honorarios' : 'Recaudo'}
                </button>
              ))}
              <span style={{ color: 'var(--z-border)', margin: '0 2px' }}>·</span>
              {(['valor', 'proyeccion', 'meta'] as const).map(c => (
                <button key={c} onClick={() => setCriterio(c)} style={{
                  padding: '3px 10px', borderRadius: 6, border: '1px solid var(--z-border)',
                  background: criterio === c ? C.naranja : 'transparent',
                  color: criterio === c ? '#fff' : 'var(--z-text-muted)',
                  cursor: 'pointer', fontSize: 12, fontWeight: criterio === c ? 600 : 400,
                }}>
                  {c === 'valor' ? 'Valor' : c === 'proyeccion' ? 'Proyección' : 'Meta'}
                </button>
              ))}
            </div>
          </div>
        </div>
        <div ref={refGrafico}>
          <ResponsiveContainer width="100%" height={280}>
            <BarChart data={chartData} margin={{ left: 10, right: 10, top: 4, bottom: 55 }}>
              <CartesianGrid vertical={false} stroke="var(--z-border)" />
              <XAxis dataKey="label" tick={{ fontSize: 11, fill: 'var(--z-text-muted)', angle: -25, textAnchor: 'end' }} interval={0} />
              <YAxis tickFormatter={v => cop(v)} tick={{ fontSize: 11, fill: 'var(--z-text-muted)' }} width={110} />
              <Tooltip content={<TooltipCOP />} />
              <Bar dataKey="metricaValor" name={metrica === 'honorarios' ? 'Honorarios' : 'Recaudo'} fill={metrica === 'honorarios' ? C.honorarios : C.brand} radius={[4, 4, 0, 0]} />
              <Bar dataKey="deberiaHoy"   name="Debería hoy" fill={C.proyeccion} radius={[4, 4, 0, 0]} />
              <Bar dataKey="valorMeta"    name="Meta"        fill={C.meta}       radius={[4, 4, 0, 0]} />
              {promDeberiaHoy > 0 && (
                <ReferenceLine y={promDeberiaHoy} stroke={C.proyeccion} strokeDasharray="4 3" strokeWidth={1.5}
                  label={{ value: `Prom. ${cop(promDeberiaHoy)}`, position: 'insideTopRight', fontSize: 11, fill: C.proyeccion }} />
              )}
            </BarChart>
          </ResponsiveContainer>
        </div>
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
              {th('Asignaciones',      'capacidad',       'center')}
              {th('Recaudo',           'recaudo')}
              {th('Honorarios',        'honorarios')}
              <th style={{ ...TH, textAlign: 'center' }}>Tipo Meta</th>
              {th('Meta',              'valorMeta')}
              {th('Debería hoy',       'deberiaLlevarHoy')}
              {th('Proyección cierre', 'proyeccionCierre')}
              {th('Cump.',             'cumplimiento',    'center')}
            </tr>
          </thead>
          <tbody>
            {(sorted as unknown as LiderAgregado[]).map(l => {
              const proyOk = l.proyeccionCierre >= l.valorMeta && l.valorMeta > 0;
              return (
                <tr key={l.liderID}>
                  <td style={{ ...TD, textAlign: 'left', fontWeight: 500, color: 'var(--z-text-primary)' }}>{l.nombreLider}</td>
                  <td style={{ ...TD, textAlign: 'center', color: 'var(--z-text-muted)' }}>{l.carteras}</td>
                  <td style={{ ...TD, textAlign: 'center' }}>{l.asesores}</td>
                  <td style={{ ...TD, textAlign: 'center' }}>{l.capacidad.toLocaleString('es-CO')}</td>
                  <td style={{ ...TD, textAlign: 'right', color: C.brand, fontWeight: 600 }}>{cop(l.recaudo)}</td>
                  <td style={{ ...TD, textAlign: 'right', color: C.honorarios }}>{cop(l.honorarios)}</td>
                  <td style={{ ...TD, textAlign: 'center' }}><ChipTipoMeta tipo={l.tipoMeta} /></td>
                  <td style={{ ...TD, textAlign: 'right', color: 'var(--z-text-muted)' }}>{l.tieneMeta ? cop(l.valorMeta) : '—'}</td>
                  <td style={{ ...TD, textAlign: 'right', color: 'var(--z-text-muted)' }}>{l.tieneMeta ? cop(l.deberiaLlevarHoy) : '—'}</td>
                  <td style={{ ...TD, textAlign: 'right', color: proyOk ? C.verde : C.naranja, fontWeight: 600 }}>{cop(l.proyeccionCierre)}</td>
                  <td style={{ ...TD, textAlign: 'center' }}>
                    <Semaforo valorActual={l.honorarios} valorMeta={l.valorMeta} />
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
// ║  TAB — CARTERAS (desglose por Líder-Cartera o por Cartera)                  ║
// ╚══════════════════════════════════════════════════════════════════════════════╝
type VistaCarteras = 'lider-cartera' | 'cartera';

const COSTO_ASESOR = 4_100_000;

interface CarteraAgregada {
  carteraID:        number;
  nombreCartera:    string;
  lideres:          number;
  asesores:         number;
  capacidad:        number;
  valorMeta:        number;
  tipoMeta:         'Recaudo' | 'Honorarios' | null;
  recaudo:          number;
  honorarios:       number;
  deberiaLlevarHoy: number;
  proyeccionCierre: number;
  tieneMeta:        boolean;
  cumplimiento:     number;
  centroCostos:     number;
}

function TabCarterasDetalle({ año, mes, onRegisterDownload }: {
  año: number; mes: number; onRegisterDownload?: DownloadRegistrar;
}) {
  const { usuario } = useAuth();
  const esLider = usuario?.rol === 'Líder';
  const [vista, setVista] = useState<VistaCarteras>('lider-cartera');
  const [metricaC,  setMetricaC]  = useState<'honorarios' | 'recaudo'>('honorarios');
  const [criterioC, setCriterioC] = useState<'valor' | 'proyeccion' | 'meta'>('valor');
  const [filtroLider, setFiltroLider] = useState(() => esLider ? String(usuario?.liderID ?? '') : '');
  const refGrafico = useRef<HTMLDivElement>(null);
  type FilaLC = InformeLiderCartera & { cumplimiento: number; centroCostos: number };
  const latestRef  = useRef<{ filasPorLiderCartera: FilaLC[]; filasCartera: CarteraAgregada[]; vista: VistaCarteras; esLider: boolean }>({ filasPorLiderCartera: [], filasCartera: [], vista: 'lider-cartera', esLider: false });

  const { data, isLoading, isError } = useQuery({
    queryKey: ['inf-lideres', año, mes],
    queryFn:  () => getInformeLideres(año, mes),
  });

  const { optsLider, filasPorLiderCartera, filasCartera } = useMemo(() => {
    if (!data) return { optsLider: [], filasPorLiderCartera: [], filasCartera: [] };

    const optsLider = Array.from(
      new Map(data.liderCarteras.map(f => [f.liderID, f.nombreLider])).entries()
    ).map(([value, label]) => ({ value: String(value), label }));

    const fuenteFiltrada = filtroLider
      ? data.liderCarteras.filter(f => String(f.liderID) === filtroLider)
      : data.liderCarteras;

    // ── Vista Líder-Cartera ────────────────────────────────────────────────────
    const filasPorLiderCartera = fuenteFiltrada.map(f => ({
      ...f,
      cumplimiento: f.valorMeta > 0 ? f.honorarios / f.valorMeta : 0,
      centroCostos: f.totalAsesores * COSTO_ASESOR,
    }));

    // ── Vista Cartera (agrupar todos los líderes de una misma cartera) ─────────
    const porCartera = new Map<number, CarteraAgregada>();
    for (const f of fuenteFiltrada) {
      const prev = porCartera.get(f.carteraID);
      if (prev) {
        prev.lideres += 1; prev.asesores += f.totalAsesores; prev.capacidad += f.capacidad;
        prev.valorMeta += f.valorMeta;
        if (!prev.tipoMeta && f.tipoMeta) prev.tipoMeta = f.tipoMeta;
        prev.recaudo += f.recaudo; prev.honorarios += f.honorarios;
        prev.deberiaLlevarHoy += f.deberiaLlevarHoy; prev.proyeccionCierre += f.proyeccionCierre;
        prev.tieneMeta = prev.tieneMeta || f.tieneMeta;
        prev.centroCostos += f.totalAsesores * COSTO_ASESOR;
      } else {
        porCartera.set(f.carteraID, {
          carteraID: f.carteraID, nombreCartera: f.nombreCartera,
          lideres: 1, asesores: f.totalAsesores, capacidad: f.capacidad,
          valorMeta: f.valorMeta, tipoMeta: f.tipoMeta,
          recaudo: f.recaudo, honorarios: f.honorarios,
          deberiaLlevarHoy: f.deberiaLlevarHoy, proyeccionCierre: f.proyeccionCierre,
          tieneMeta: f.tieneMeta, cumplimiento: 0,
          centroCostos: f.totalAsesores * COSTO_ASESOR,
        });
      }
    }
    const filasCartera: CarteraAgregada[] = Array.from(porCartera.values()).map(c => ({
      ...c, cumplimiento: c.valorMeta > 0 ? c.honorarios / c.valorMeta : 0,
    }));

    return { optsLider, filasPorLiderCartera, filasCartera };
  }, [data, filtroLider]);

  latestRef.current = { filasPorLiderCartera, filasCartera, vista, esLider };

  useEffect(() => {
    if (!onRegisterDownload) return;
    const nombreMes = MESES[mes - 1];
    onRegisterDownload(() => {
      const { filasPorLiderCartera, filasCartera, vista, esLider } = latestRef.current;
      const filas = vista === 'lider-cartera' ? filasPorLiderCartera : filasCartera;
      if (filas.length === 0) return Promise.resolve();

      let headCols: string[];
      let body: (string | number | null)[][];

      if (vista === 'lider-cartera') {
        headCols = [...(esLider ? [] : ['Líder']), 'Cartera', 'Asesores',
          'Centro Costos', 'Asignaciones', 'Recaudo', 'Honorarios',
          'Tipo Meta', 'Meta', 'Debería hoy', 'Proyección', 'Cump.'];
        body = filasPorLiderCartera.map(f => [
          ...(esLider ? [] : [f.nombreLider]),
          f.nombreCartera, f.totalAsesores,
          cop(f.centroCostos), f.capacidad.toLocaleString('es-CO'),
          cop(f.recaudo), cop(f.honorarios),
          f.tipoMeta ?? '—',
          f.tieneMeta ? cop(f.valorMeta) : '—',
          f.tieneMeta ? cop(f.deberiaLlevarHoy) : '—',
          cop(f.proyeccionCierre),
          f.valorMeta > 0 ? f.honorarios / f.valorMeta : -1,
        ]);
      } else {
        headCols = ['Cartera', 'Líderes', 'Asesores', 'Centro Costos', 'Asignaciones',
          'Recaudo', 'Honorarios', 'Tipo Meta', 'Meta', 'Debería hoy', 'Proyección', 'Cump.'];
        body = filasCartera.map(c => [
          c.nombreCartera, c.lideres, c.asesores,
          cop(c.centroCostos), c.capacidad.toLocaleString('es-CO'),
          cop(c.recaudo), cop(c.honorarios),
          c.tipoMeta ?? '—',
          c.tieneMeta ? cop(c.valorMeta) : '—',
          c.tieneMeta ? cop(c.deberiaLlevarHoy) : '—',
          cop(c.proyeccionCierre),
          c.valorMeta > 0 ? c.honorarios / c.valorMeta : -1,
        ]);
      }

      const cumpColIndex = headCols.length - 1;
      const offset = (vista === 'lider-cartera' && !esLider) ? 1 : 0;
      const colStyles: Record<number, { halign: 'left' | 'center' | 'right' }> = {
        [0 + offset]: { halign: 'left' },
        [1 + offset]: { halign: 'center' },
        [3 + offset]: { halign: 'center' },
        [6 + offset]: { halign: 'center' },
      };
      return buildExportarPDF({
        tituloArchivo: `Informe_Carteras_${nombreMes}_${año}`,
        tituloPDF:     `Informe Carteras — ${nombreMes} ${año}`,
        subtitulo:     `Generado el ${new Date().toLocaleDateString('es-CO', { day: 'numeric', month: 'long', year: 'numeric' })}`,
        pdfTable: { head: [headCols], body, cumpColIndex, colStyles },
        refGrafico,
      });
    });
  }, [onRegisterDownload, año, mes]);

  if (isLoading) return <Cargando />;
  if (isError || !data) return <ErrorMsg />;

  const filas = vista === 'lider-cartera' ? filasPorLiderCartera : filasCartera;
  const totalHonorarios = filas.reduce((s: number, f: typeof filas[0]) => s + f.honorarios, 0);

  const campoOrdenC = criterioC === 'valor' ? metricaC : criterioC === 'proyeccion' ? 'proyeccionCierre' : 'valorMeta';

  const chartData = [...filas]
    .sort((a: typeof filas[0], b: typeof filas[0]) => (b as unknown as Record<string, number>)[campoOrdenC] - (a as unknown as Record<string, number>)[campoOrdenC])
    .map((f: typeof filas[0]) => ({
      nombre:           vista === 'lider-cartera'
                          ? `${(f as typeof filasPorLiderCartera[0]).nombreLider} / ${f.nombreCartera}`
                          : f.nombreCartera,
      metricaValor:     metricaC === 'honorarios' ? f.honorarios : f.recaudo,
      deberiaLlevarHoy: f.deberiaLlevarHoy,
      valorMeta:        f.valorMeta,
    }));

  const promDeberiaHoy = filas.length > 0
    ? filas.reduce((s: number, f: typeof filas[0]) => s + f.deberiaLlevarHoy, 0) / filas.length
    : 0;

  return (
    <div style={{ display: 'flex', flexDirection: 'column', gap: 20 }}>

      {/* KPIs */}
      <div style={{ display: 'flex', gap: 12, flexWrap: 'wrap' }}>
        <KpiCard label="Total Honorarios" value={cop(totalHonorarios)} color={C.honorarios} />
        <KpiCard label="Carteras"         value={String(filasCartera.length)} />
        {vista === 'lider-cartera' && (
          <KpiCard label="Combinaciones Líder-Cartera" value={String(filasPorLiderCartera.length)} />
        )}
      </div>

      {/* Gráfico */}
      <div style={{ background: 'var(--z-bg-card)', border: '1px solid var(--z-border)', borderRadius: 10, padding: '20px 16px' }}>
        <div style={{ display: 'flex', alignItems: 'center', justifyContent: 'space-between', flexWrap: 'wrap', gap: 12, marginBottom: 16 }}>
          <div style={{ display: 'flex', alignItems: 'center', gap: 12, flexWrap: 'wrap' }}>

            {/* Toggle de vista */}
            <div style={{ display: 'flex', borderRadius: 8, overflow: 'hidden', border: '1px solid var(--z-border)' }}>
              {(['lider-cartera', 'cartera'] as VistaCarteras[]).map(v => {
                const active = vista === v;
                return (
                  <button key={v} onClick={() => setVista(v)} style={{
                    padding: '4px 14px', fontSize: 12, fontWeight: active ? 600 : 400,
                    background: active ? C.naranja : 'transparent',
                    color: active ? '#fff' : 'var(--z-text-muted)',
                    border: 'none', cursor: 'pointer',
                  }}>
                    {v === 'lider-cartera' ? 'Líder-Cartera' : 'Cartera'}
                  </button>
                );
              })}
            </div>

            {!esLider && (
              <FiltroSelect
                label="Líder:"
                value={filtroLider}
                onChange={setFiltroLider}
                options={optsLider}
                placeholder="Todos los líderes"
              />
            )}

            <LeyendaGrafico items={[
              { color: metricaC === 'honorarios' ? C.honorarios : C.brand, label: metricaC === 'honorarios' ? 'Honorarios' : 'Recaudo' },
              { color: C.proyeccion, label: 'Debería hoy' },
              { color: C.meta,       label: 'Meta' },
            ]} />
          </div>

          <div style={{ display: 'flex', alignItems: 'center', gap: 6, flexWrap: 'wrap' }}>
            <span style={{ fontSize: 12, fontWeight: 600, color: 'var(--z-text-muted)' }}>Ordenar por</span>
            {(['honorarios', 'recaudo'] as const).map(m => (
              <button key={m} onClick={() => setMetricaC(m)} style={{
                padding: '3px 10px', borderRadius: 6, border: '1px solid var(--z-border)',
                background: metricaC === m ? C.naranja : 'transparent',
                color: metricaC === m ? '#fff' : 'var(--z-text-muted)',
                cursor: 'pointer', fontSize: 12, fontWeight: metricaC === m ? 600 : 400,
              }}>
                {m === 'honorarios' ? 'Honorarios' : 'Recaudo'}
              </button>
            ))}
            <span style={{ color: 'var(--z-border)', margin: '0 2px' }}>·</span>
            {(['valor', 'proyeccion', 'meta'] as const).map(c => (
              <button key={c} onClick={() => setCriterioC(c)} style={{
                padding: '3px 10px', borderRadius: 6, border: '1px solid var(--z-border)',
                background: criterioC === c ? C.naranja : 'transparent',
                color: criterioC === c ? '#fff' : 'var(--z-text-muted)',
                cursor: 'pointer', fontSize: 12, fontWeight: criterioC === c ? 600 : 400,
              }}>
                {c === 'valor' ? 'Valor' : c === 'proyeccion' ? 'Proyección' : 'Meta'}
              </button>
            ))}
          </div>
        </div>

        <div ref={refGrafico}>
          <ResponsiveContainer width="100%" height={280}>
            <BarChart data={chartData} margin={{ left: 10, right: 10, top: 4, bottom: 70 }}>
              <CartesianGrid vertical={false} stroke="var(--z-border)" />
              <XAxis dataKey="nombre" tick={{ fontSize: 11, fill: 'var(--z-text-muted)', angle: -35, textAnchor: 'end' }} interval={0} />
              <YAxis tickFormatter={v => cop(v)} tick={{ fontSize: 11, fill: 'var(--z-text-muted)' }} width={110} />
              <Tooltip content={<TooltipCOP />} />
              <Bar dataKey="metricaValor"     name={metricaC === 'honorarios' ? 'Honorarios' : 'Recaudo'} fill={metricaC === 'honorarios' ? C.honorarios : C.brand} radius={[4, 4, 0, 0]} />
              <Bar dataKey="deberiaLlevarHoy" name="Debería hoy" fill={C.proyeccion} radius={[4, 4, 0, 0]} />
              <Bar dataKey="valorMeta"        name="Meta"        fill={C.meta}       radius={[4, 4, 0, 0]} />
              {promDeberiaHoy > 0 && (
                <ReferenceLine y={promDeberiaHoy} stroke={C.proyeccion} strokeDasharray="4 3" strokeWidth={1.5}
                  label={{ value: `Prom. ${cop(promDeberiaHoy)}`, position: 'insideTopRight', fontSize: 11, fill: C.proyeccion }} />
              )}
            </BarChart>
          </ResponsiveContainer>
        </div>
      </div>

      {/* Tabla */}
      {vista === 'lider-cartera'
        ? <TablaCarterasLiderCartera filas={filasPorLiderCartera} esLider={esLider} />
        : <TablaCarterasAgrupada     filas={filasCartera} />
      }
    </div>
  );
}

function TablaCarterasLiderCartera({ filas, esLider }: { filas: (InformeLiderCartera & { cumplimiento: number })[]; esLider: boolean }) {
  const { sorted, sortKey, sortDir, handleSort } = useSortable(filas as unknown as Record<string, unknown>[]);
  const th = (label: string, k: string, align?: 'left' | 'right' | 'center') => (
    <ThSort label={label} k={k} sortKey={sortKey as string | null} sortDir={sortDir} onSort={handleSort} align={align} />
  );
  return (
    <div style={{ background: 'var(--z-bg-card)', border: '1px solid var(--z-border)', borderRadius: 10, overflow: 'hidden' }}>
      <div style={{ overflowX: 'auto' }}>
        <table style={{ width: '100%', borderCollapse: 'collapse', fontSize: 13 }}>
          <thead>
            <tr style={{ background: 'var(--z-bg-page)', borderBottom: '1px solid var(--z-border)' }}>
              {!esLider && th('Líder',  'nombreLider',     'left')}
              {th('Cartera',           'nombreCartera',   'left')}
              {th('Asesores',          'totalAsesores',   'center')}
              {th('Centro de costos',  'centroCostos')}
              {th('Asignaciones',      'capacidad',       'center')}
              {th('Recaudo',           'recaudo')}
              {th('Honorarios',        'honorarios')}
              <th style={{ ...TH, textAlign: 'center' }}>Tipo Meta</th>
              {th('Meta',              'valorMeta')}
              {th('Debería hoy',       'deberiaLlevarHoy')}
              {th('Proyección',        'proyeccionCierre')}
              {th('Cump.',             'cumplimiento',    'center')}
            </tr>
          </thead>
          <tbody>
            {(sorted as unknown as (InformeLiderCartera & { cumplimiento: number; centroCostos: number })[]).map(f => (
              <tr key={`${f.liderID}-${f.carteraID}`}>
                {!esLider && <td style={{ ...TD, textAlign: 'left', fontWeight: 500, color: 'var(--z-text-primary)' }}>{f.nombreLider}</td>}
                <td style={{ ...TD, textAlign: 'left', color: 'var(--z-text-muted)' }}>{f.nombreCartera}</td>
                <td style={{ ...TD, textAlign: 'center' }}>{f.totalAsesores}</td>
                <td style={{ ...TD, textAlign: 'right', color: 'var(--z-text-muted)' }}>{cop(f.centroCostos)}</td>
                <td style={{ ...TD, textAlign: 'center' }}>{f.capacidad.toLocaleString('es-CO')}</td>
                <td style={{ ...TD, textAlign: 'right', color: C.brand, fontWeight: 600 }}>{cop(f.recaudo)}</td>
                <td style={{ ...TD, textAlign: 'right', color: C.honorarios }}>{cop(f.honorarios)}</td>
                <td style={{ ...TD, textAlign: 'center' }}><ChipTipoMeta tipo={f.tipoMeta} /></td>
                <td style={{ ...TD, textAlign: 'right', color: 'var(--z-text-muted)' }}>{f.tieneMeta ? cop(f.valorMeta) : '—'}</td>
                <td style={{ ...TD, textAlign: 'right', color: 'var(--z-text-muted)' }}>{f.tieneMeta ? cop(f.deberiaLlevarHoy) : '—'}</td>
                <td style={{ ...TD, textAlign: 'right', color: f.proyeccionCierre >= f.valorMeta && f.valorMeta > 0 ? C.verde : C.naranja, fontWeight: 600 }}>{cop(f.proyeccionCierre)}</td>
                <td style={{ ...TD, textAlign: 'center' }}>
                  <Semaforo valorActual={f.honorarios} valorMeta={f.valorMeta} />
                </td>
              </tr>
            ))}
          </tbody>
        </table>
      </div>
    </div>
  );
}

function TablaCarterasAgrupada({ filas }: { filas: CarteraAgregada[] }) {
  const { sorted, sortKey, sortDir, handleSort } = useSortable(filas as unknown as Record<string, unknown>[]);
  const th = (label: string, k: string, align?: 'left' | 'right' | 'center') => (
    <ThSort label={label} k={k} sortKey={sortKey as string | null} sortDir={sortDir} onSort={handleSort} align={align} />
  );
  return (
    <div style={{ background: 'var(--z-bg-card)', border: '1px solid var(--z-border)', borderRadius: 10, overflow: 'hidden' }}>
      <div style={{ overflowX: 'auto' }}>
        <table style={{ width: '100%', borderCollapse: 'collapse', fontSize: 13 }}>
          <thead>
            <tr style={{ background: 'var(--z-bg-page)', borderBottom: '1px solid var(--z-border)' }}>
              {th('Cartera',           'nombreCartera',   'left')}
              {th('Líderes',           'lideres',         'center')}
              {th('Asesores',          'asesores',        'center')}
              {th('Centro de costos',  'centroCostos')}
              {th('Asignaciones',      'capacidad',       'center')}
              {th('Recaudo',           'recaudo')}
              {th('Honorarios',        'honorarios')}
              <th style={{ ...TH, textAlign: 'center' }}>Tipo Meta</th>
              {th('Meta',              'valorMeta')}
              {th('Debería hoy',       'deberiaLlevarHoy')}
              {th('Proyección',        'proyeccionCierre')}
              {th('Cump.',             'cumplimiento',    'center')}
            </tr>
          </thead>
          <tbody>
            {(sorted as unknown as CarteraAgregada[]).map(c => (
              <tr key={c.carteraID}>
                <td style={{ ...TD, textAlign: 'left', fontWeight: 500, color: 'var(--z-text-primary)' }}>{c.nombreCartera}</td>
                <td style={{ ...TD, textAlign: 'center', color: 'var(--z-text-muted)' }}>{c.lideres}</td>
                <td style={{ ...TD, textAlign: 'center' }}>{c.asesores}</td>
                <td style={{ ...TD, textAlign: 'right', color: 'var(--z-text-muted)' }}>{cop(c.centroCostos)}</td>
                <td style={{ ...TD, textAlign: 'center' }}>{c.capacidad.toLocaleString('es-CO')}</td>
                <td style={{ ...TD, textAlign: 'right', color: C.brand, fontWeight: 600 }}>{cop(c.recaudo)}</td>
                <td style={{ ...TD, textAlign: 'right', color: C.honorarios }}>{cop(c.honorarios)}</td>
                <td style={{ ...TD, textAlign: 'center' }}><ChipTipoMeta tipo={c.tipoMeta} /></td>
                <td style={{ ...TD, textAlign: 'right', color: 'var(--z-text-muted)' }}>{c.tieneMeta ? cop(c.valorMeta) : '—'}</td>
                <td style={{ ...TD, textAlign: 'right', color: 'var(--z-text-muted)' }}>{c.tieneMeta ? cop(c.deberiaLlevarHoy) : '—'}</td>
                <td style={{ ...TD, textAlign: 'right', color: c.proyeccionCierre >= c.valorMeta && c.valorMeta > 0 ? C.verde : C.naranja, fontWeight: 600 }}>{cop(c.proyeccionCierre)}</td>
                <td style={{ ...TD, textAlign: 'center' }}>
                  <Semaforo valorActual={c.honorarios} valorMeta={c.valorMeta} />
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
// ║  TAB — COMPARATIVO LIBRE                                                    ║
// ╚══════════════════════════════════════════════════════════════════════════════╝
interface FilaComparativo {
  carteraID:    number;
  nombre:       string;
  valA:         number; honA: number;
  valB:         number; honB: number;
  valC:         number; honC: number;
}

function TabComparativo({ onRegisterDownload }: { onRegisterDownload?: DownloadRegistrar }) {
  const refGrafico = useRef<HTMLDivElement>(null);
  const latestRef  = useRef<{ filas: FilaComparativo[]; labelA: string; labelB: string; labelC: string; numPeriodos: 2 | 3 }>({ filas: [], labelA: '', labelB: '', labelC: '', numPeriodos: 2 });
  const [numPeriodos, setNumPeriodos] = useState<2 | 3>(2);
  const [añoA, setAñoA] = useState(AÑO_DOS_ANTES);
  const [mesA,  setMesA]  = useState(MES_DOS_ANTES);
  const [añoB, setAñoB] = useState(AÑO_ANTERIOR);
  const [mesB,  setMesB]  = useState(MES_ANTERIOR);
  const [añoC, setAñoC] = useState(AÑO_ACTUAL);
  const [mesC,  setMesC]  = useState(MES_ACTUAL);

  const qAB = useQuery({
    queryKey: ['inf-comp-ab', añoA, mesA, añoB, mesB],
    queryFn:  () => getInformeCarteras(añoA, mesA, añoB, mesB),
  });
  const qBC = useQuery({
    queryKey: ['inf-comp-bc', añoB, mesB, añoC, mesC],
    queryFn:  () => getInformeCarteras(añoB, mesB, añoC, mesC),
    enabled:  numPeriodos === 3,
  });

  const isLoading = qAB.isLoading || (numPeriodos === 3 && qBC.isLoading);
  const isError   = qAB.isError   || (numPeriodos === 3 && qBC.isError);

  const labelA = qAB.data?.periodoA.nombre ?? `${MESES[mesA - 1]} ${añoA}`;
  const labelB = qAB.data?.periodoB.nombre ?? `${MESES[mesB - 1]} ${añoB}`;
  const labelC = qBC.data?.periodoB.nombre ?? `${MESES[mesC - 1]} ${añoC}`;

  // Fusionar por carteraID
  const filas = useMemo<FilaComparativo[]>(() => {
    if (!qAB.data) return [];
    const map = new Map<number, FilaComparativo>();
    for (const c of qAB.data.carteras) {
      map.set(c.carteraID, {
        carteraID: c.carteraID,
        nombre:    c.nombreCartera,
        valA: c.recaudoA,  honA: c.honorariosA,
        valB: c.recaudoB,  honB: c.honorariosB,
        valC: 0,           honC: 0,
      });
    }
    if (numPeriodos === 3 && qBC.data) {
      for (const c of qBC.data.carteras) {
        const prev = map.get(c.carteraID);
        if (prev) { prev.valC = c.recaudoB; prev.honC = c.honorariosB; }
        else map.set(c.carteraID, {
          carteraID: c.carteraID, nombre: c.nombreCartera,
          valA: 0, honA: 0, valB: c.recaudoA, honB: c.honorariosA,
          valC: c.recaudoB, honC: c.honorariosB,
        });
      }
    }
    return Array.from(map.values());
  }, [qAB.data, qBC.data, numPeriodos]);

  latestRef.current = { filas, labelA, labelB, labelC, numPeriodos };

  useEffect(() => {
    if (!onRegisterDownload) return;
    onRegisterDownload(() => {
      const { filas, labelA, labelB, labelC, numPeriodos } = latestRef.current;
      if (filas.length === 0) return Promise.resolve();

      const partes = numPeriodos === 3 ? [labelA, labelB, labelC] : [labelA, labelB];
      const tituloBase = partes.join('_vs_').replace(/\s/g, '_').replace(/[^a-zA-ZÀ-ÿ0-9_-]/g, '');
      const headCols = [
        'Cartera',
        `Rec. ${labelA}`, `Hon. ${labelA}`,
        `Rec. ${labelB}`, `Hon. ${labelB}`,
        ...(numPeriodos === 3 ? [`Rec. ${labelC}`, `Hon. ${labelC}`] : []),
        'Var. A→B',
        ...(numPeriodos === 3 ? ['Var. B→C'] : []),
      ];
      const body = filas.map(f => {
        const varAB = f.valA > 0 ? (f.valB - f.valA) / f.valA : null;
        const varBC = f.valB > 0 ? (f.valC - f.valB) / f.valB : null;
        return [
          f.nombre,
          cop(f.valA), cop(f.honA),
          cop(f.valB), cop(f.honB),
          ...(numPeriodos === 3 ? [cop(f.valC), cop(f.honC)] : []),
          varAB !== null ? `${varAB > 0 ? '+' : ''}${(varAB * 100).toFixed(1)}%` : '—',
          ...(numPeriodos === 3 ? [varBC !== null ? `${varBC > 0 ? '+' : ''}${(varBC * 100).toFixed(1)}%` : '—'] : []),
        ];
      });
      const varABIdx = numPeriodos === 3 ? 7 : 5;
      const colStyles: Record<number, { halign: 'left' | 'center' | 'right' }> = {
        0: { halign: 'left' },
        [varABIdx]: { halign: 'center' },
        ...(numPeriodos === 3 ? { [varABIdx + 1]: { halign: 'center' } } : {}),
      };
      return buildExportarPDF({
        tituloArchivo: `Informe_Comparativo_${tituloBase}`,
        tituloPDF:     `Informe Comparativo — ${partes.join(' vs ')}`,
        subtitulo:     `Generado el ${new Date().toLocaleDateString('es-CO', { day: 'numeric', month: 'long', year: 'numeric' })}`,
        pdfTable: { head: [headCols], body, cumpColIndex: -1, colStyles },
        refGrafico,
      });
    });
  }, [onRegisterDownload]);

  const chartData = filas.map(f => ({
    nombre:    f.nombre.length > 22 ? f.nombre.slice(0, 22) + '…' : f.nombre,
    [labelA]:  f.valA,
    [labelB]:  f.valB,
    ...(numPeriodos === 3 ? { [labelC]: f.valC } : {}),
  }));

  const totalA = filas.reduce((s, f) => s + f.valA, 0);
  const totalB = filas.reduce((s, f) => s + f.valB, 0);
  const totalC = filas.reduce((s, f) => s + f.valC, 0);

  return (
    <div style={{ display: 'flex', flexDirection: 'column', gap: 20 }}>

      {/* Selectores + toggle 2/3 meses */}
      <div style={{ background: 'var(--z-bg-card)', border: '1px solid var(--z-border)', borderRadius: 10, padding: '14px 20px', display: 'flex', gap: 20, flexWrap: 'wrap', alignItems: 'center' }}>

        {/* Toggle 2 / 3 períodos */}
        <div style={{ display: 'flex', borderRadius: 8, overflow: 'hidden', border: '1px solid var(--z-border)', flexShrink: 0 }}>
          {([2, 3] as (2 | 3)[]).map(n => {
            const active = numPeriodos === n;
            return (
              <button key={n} onClick={() => setNumPeriodos(n)} style={{
                padding: '4px 14px', fontSize: 12, fontWeight: active ? 600 : 400,
                background: active ? C.naranja : 'transparent',
                color: active ? '#fff' : 'var(--z-text-muted)',
                border: 'none', cursor: 'pointer',
              }}>
                {n} meses
              </button>
            );
          })}
        </div>

        <span style={{ color: 'var(--z-border)', fontSize: 18 }}>|</span>

        <div style={{ display: 'flex', alignItems: 'center', gap: 6 }}>
          <span style={{ width: 10, height: 10, borderRadius: 2, background: C.mesA, display: 'inline-block' }} />
          <SelectorPeriodo año={añoA} mes={mesA} onAño={setAñoA} onMes={setMesA} label="Mes A:" />
        </div>
        <span style={{ color: 'var(--z-text-muted)' }}>vs</span>
        <div style={{ display: 'flex', alignItems: 'center', gap: 6 }}>
          <span style={{ width: 10, height: 10, borderRadius: 2, background: C.mesB, display: 'inline-block' }} />
          <SelectorPeriodo año={añoB} mes={mesB} onAño={setAñoB} onMes={setMesB} label="Mes B:" />
        </div>
        {numPeriodos === 3 && (
          <>
            <span style={{ color: 'var(--z-text-muted)' }}>vs</span>
            <div style={{ display: 'flex', alignItems: 'center', gap: 6 }}>
              <span style={{ width: 10, height: 10, borderRadius: 2, background: C.mesC, display: 'inline-block' }} />
              <SelectorPeriodo año={añoC} mes={mesC} onAño={setAñoC} onMes={setMesC} label="Mes C:" />
            </div>
          </>
        )}
      </div>

      {isLoading && <Cargando />}
      {isError   && <ErrorMsg />}

      {!isLoading && !isError && filas.length > 0 && (
        <>
          {/* KPIs */}
          <div style={{ display: 'flex', gap: 12, flexWrap: 'wrap' }}>
            <KpiCard label={labelA} value={cop(totalA)} color={C.mesA} />
            <KpiCard label={labelB} value={cop(totalB)} color={C.mesB} />
            {numPeriodos === 3 && <KpiCard label={labelC} value={cop(totalC)} color={C.mesC} />}
            <KpiCard
              label="Var. A→B"
              value={totalA > 0 ? `${((totalB - totalA) / totalA * 100).toFixed(1)}%` : '—'}
              color={totalB >= totalA ? C.verde : C.rojo}
            />
            {numPeriodos === 3 && (
              <KpiCard
                label="Var. B→C"
                value={totalB > 0 ? `${((totalC - totalB) / totalB * 100).toFixed(1)}%` : '—'}
                color={totalC >= totalB ? C.verde : C.rojo}
              />
            )}
          </div>

          {/* Gráfico */}
          <div style={{ background: 'var(--z-bg-card)', border: '1px solid var(--z-border)', borderRadius: 10, padding: '20px 16px' }}>
            <p style={{ fontSize: 13, fontWeight: 600, color: 'var(--z-text-primary)', marginBottom: 16 }}>
              Recaudo por cartera — {numPeriodos === 3 ? `${labelA} · ${labelB} · ${labelC}` : `${labelA} vs ${labelB}`}
            </p>
            <div ref={refGrafico}>
              <ResponsiveContainer width="100%" height={Math.max(280, chartData.length * 60)}>
                <BarChart data={chartData} margin={{ left: 10, right: 10, top: 4, bottom: 50 }}>
                  <CartesianGrid vertical={false} stroke="var(--z-border)" />
                  <XAxis dataKey="nombre" tick={{ fontSize: 11, fill: 'var(--z-text-muted)' }} angle={-30} textAnchor="end" interval={0} />
                  <YAxis tickFormatter={v => cop(v)} tick={{ fontSize: 11, fill: 'var(--z-text-muted)' }} width={110} />
                  <Tooltip content={<TooltipCOP />} />
                  <Bar dataKey={labelA} fill={C.mesA} radius={[4, 4, 0, 0]} />
                  <Bar dataKey={labelB} fill={C.mesB} radius={[4, 4, 0, 0]} />
                  {numPeriodos === 3 && <Bar dataKey={labelC} fill={C.mesC} radius={[4, 4, 0, 0]} />}
                </BarChart>
              </ResponsiveContainer>
            </div>
          </div>

          <TablaComparativo filas={filas} labelA={labelA} labelB={labelB} labelC={numPeriodos === 3 ? labelC : null} />
        </>
      )}
    </div>
  );
}

function TablaComparativo({ filas, labelA, labelB, labelC }: {
  filas: FilaComparativo[]; labelA: string; labelB: string; labelC: string | null;
}) {
  const { sorted, sortKey, sortDir, handleSort } = useSortable(filas as unknown as Record<string, unknown>[]);
  const th = (label: string, k: string, align?: 'left' | 'right' | 'center') => (
    <ThSort label={label} k={k} sortKey={sortKey as string | null} sortDir={sortDir} onSort={handleSort} align={align} />
  );
  return (
    <div style={{ background: 'var(--z-bg-card)', border: '1px solid var(--z-border)', borderRadius: 10, overflow: 'hidden' }}>
      <div style={{ overflowX: 'auto' }}>
        <table style={{ width: '100%', borderCollapse: 'collapse', fontSize: 13 }}>
          <thead>
            <tr style={{ background: 'var(--z-bg-page)', borderBottom: '1px solid var(--z-border)' }}>
              {th('Cartera',             'nombre',  'left')}
              {th(`Rec. ${labelA}`,      'valA')}
              {th(`Hon. ${labelA}`,      'honA')}
              {th(`Rec. ${labelB}`,      'valB')}
              {th(`Hon. ${labelB}`,      'honB')}
              {labelC && th(`Rec. ${labelC}`, 'valC')}
              {labelC && th(`Hon. ${labelC}`, 'honC')}
              {th('Var. A→B',            'valB',    'center')}
              {labelC && th('Var. B→C',  'valC',    'center')}
            </tr>
          </thead>
          <tbody>
            {(sorted as unknown as FilaComparativo[]).map(f => {
              const varAB = f.valA > 0 ? ((f.valB - f.valA) / f.valA * 100) : null;
              const varBC = f.valB > 0 ? ((f.valC - f.valB) / f.valB * 100) : null;
              return (
                <tr key={f.carteraID}>
                  <td style={{ ...TD, textAlign: 'left', fontWeight: 500 }}>{f.nombre}</td>
                  <td style={{ ...TD, textAlign: 'right', color: 'var(--z-text-muted)' }}>{cop(f.valA)}</td>
                  <td style={{ ...TD, textAlign: 'right', color: 'var(--z-text-muted)' }}>{cop(f.honA)}</td>
                  <td style={{ ...TD, textAlign: 'right', color: C.mesB, fontWeight: 600 }}>{cop(f.valB)}</td>
                  <td style={{ ...TD, textAlign: 'right', color: C.mesB }}>{cop(f.honB)}</td>
                  {labelC && <td style={{ ...TD, textAlign: 'right', color: C.mesC, fontWeight: 600 }}>{cop(f.valC)}</td>}
                  {labelC && <td style={{ ...TD, textAlign: 'right', color: C.mesC }}>{cop(f.honC)}</td>}
                  <td style={{ ...TD, textAlign: 'center', color: varAB !== null && varAB >= 0 ? C.verde : C.rojo, fontWeight: 600 }}>
                    {pct(varAB)}
                  </td>
                  {labelC && (
                    <td style={{ ...TD, textAlign: 'center', color: varBC !== null && varBC >= 0 ? C.verde : C.rojo, fontWeight: 600 }}>
                      {pct(varBC)}
                    </td>
                  )}
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
  const { usuario } = useAuth();
  const esLider = usuario?.rol === 'Líder';

  const [tabActiva, setTabActiva] = useState<Tab>('agentes');
  const [año, setAño] = useState(AÑO_ACTUAL);
  const [mes, setMes] = useState(MES_ACTUAL);
  const [descargando, setDescargando] = useState(false);
  const downloadFnRef = useRef<(() => Promise<void>) | null>(null);

  const handleRegisterDownload = useCallback((fn: () => Promise<void>) => {
    downloadFnRef.current = fn;
  }, []);

  useEffect(() => { downloadFnRef.current = null; }, [tabActiva]);

  const handleDescargar = async () => {
    if (!downloadFnRef.current || descargando) return;
    setDescargando(true);
    try { await downloadFnRef.current(); }
    finally { setDescargando(false); }
  };

  const tabsVisibles = esLider ? TABS.filter(t => t.id === 'agentes') : TABS;
  const usaPeriodoGlobal = tabActiva === 'agentes' || tabActiva === 'lideres' || tabActiva === 'carteras';

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

        <div style={{ display: 'flex', alignItems: 'center', gap: 10, flexWrap: 'wrap' }}>
          {usaPeriodoGlobal && (
            <SelectorPeriodo año={año} mes={mes} onAño={setAño} onMes={setMes} />
          )}
          <button
            onClick={handleDescargar}
            disabled={descargando}
            style={{
              display: 'flex', alignItems: 'center', gap: 6,
              padding: '6px 14px', borderRadius: 7, fontSize: 13, fontWeight: 500,
              background: descargando ? '#94a3b8' : C.brand,
              color: '#fff', border: 'none',
              cursor: descargando ? 'not-allowed' : 'pointer',
              opacity: descargando ? 0.7 : 1,
              transition: 'background 0.15s',
              whiteSpace: 'nowrap',
            }}
          >
            <span style={{ fontSize: 14 }}>{descargando ? '⏳' : '↓'}</span>
            {descargando ? 'Generando…' : 'Descargar Informe'}
          </button>
        </div>
      </div>

      {/* Tabs */}
      <div style={{ display: 'flex', gap: 2, borderBottom: '1px solid var(--z-border)' }}>
        {tabsVisibles.map(t => (
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
      {tabActiva === 'agentes'     && <TabAgentes     año={año} mes={mes} onRegisterDownload={handleRegisterDownload} />}
      {tabActiva === 'lideres'     && <TabLideres     año={año} mes={mes} onRegisterDownload={handleRegisterDownload} />}
      {tabActiva === 'carteras'    && <TabCarterasDetalle año={año} mes={mes} onRegisterDownload={handleRegisterDownload} />}
      {tabActiva === 'comparativo' && <TabComparativo onRegisterDownload={handleRegisterDownload} />}
    </div>
  );
}
