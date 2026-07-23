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
  getInformeCarteraBaseGeneral,
} from '../../api/informesRecaudoApi';
import type {
  InformeAgenteMes,
  InformeLiderCartera,
} from '../../types/informesRecaudo';
import { ZDropdown } from '../../components/ZDropdown';
import {
  C, cop, MESES, KpiCard, Semaforo, ChipTipoMeta, Cargando, ErrorMsg,
  buildExportarPDF, filtrarColumnas, ModalColumnas, useSortable, ThSort, FiltroSelect,
  TooltipCOP, LeyendaGrafico, TH, TD,
} from '../../components/informes/reporteCompartido';
import type {
  PdfTableData, PdfExportDescriptor, DownloadFactory, DownloadRegistrar, SortHandlers,
} from '../../components/informes/reporteCompartido';

// ── Helpers locales (los compartidos viven en reporteCompartido) ──────────────
const pct = (v: number | null) =>
  v === null ? '—' : `${v > 0 ? '+' : ''}${v.toFixed(1)}%`;

const hoy          = new Date();
const AÑO_ACTUAL   = hoy.getFullYear();
const MES_ACTUAL   = hoy.getMonth() + 1;
const MES_ANTERIOR  = MES_ACTUAL === 1 ? 12 : MES_ACTUAL - 1;
const AÑO_ANTERIOR  = MES_ACTUAL === 1 ? AÑO_ACTUAL - 1 : AÑO_ACTUAL;
const MES_DOS_ANTES = MES_ACTUAL <= 2  ? MES_ACTUAL - 2 + 12 : MES_ACTUAL - 2;
const AÑO_DOS_ANTES = MES_ACTUAL <= 2  ? AÑO_ACTUAL - 1 : AÑO_ACTUAL;

// ── Selector de período ───────────────────────────────────────────────────────
function SelectorPeriodo({ año, mes, onAño, onMes, label }: {
  año: number; mes: number;
  onAño: (v: number) => void; onMes: (v: number) => void;
  label?: string;
}) {
  return (
    <div style={{ display: 'flex', alignItems: 'center', gap: 8 }}>
      {label && <span style={{ fontSize: 13, color: 'var(--z-text-muted)' }}>{label}</span>}
      <ZDropdown
        options={MESES.map((m, i) => ({ value: i + 1, label: m }))}
        value={mes}
        onChange={onMes}
        style={{ minWidth: 130 }}
      />
      <ZDropdown
        options={[2024, 2025, 2026, 2027].map(a => ({ value: a, label: String(a) }))}
        value={año}
        onChange={onAño}
        style={{ minWidth: 90 }}
      />
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
  // Umbral gerencial: "Asesores con menos de $X en honorarios" — filtro opcional
  // para generar el reporte de asesores por debajo del honorario exigido.
  const [umbralTexto,    setUmbralTexto]    = useState('');
  const [soloBajoUmbral, setSoloBajoUmbral] = useState(false);
  const umbral = umbralTexto ? Number(umbralTexto) : 0;
  // Ref siempre actualizado: la fn de descarga lo lee en el momento del clic (datos frescos)
  const latestRef  = useRef<{ agentesVisibles: (InformeAgenteMes & { caida: number })[]; esLider: boolean; soloBajoUmbral: boolean; umbral: number }>({ agentesVisibles: [], esLider: false, soloBajoUmbral: false, umbral: 0 });

  const { data, isLoading, isError } = useQuery({
    queryKey: ['inf-agentes', año, mes],
    queryFn:  () => getInformeAgentes(año, mes),
  });

  // Filtrado cliente-lado (líder / cartera / búsqueda) — base sobre la que se
  // calcula el conteo de asesores bajo el umbral y, si el toggle está activo,
  // se restringe además a esos asesores.
  const agentesFiltrados = useMemo(() => {
    if (!data) return [];
    const termino = busqueda.trim().toLowerCase();
    return data.agentes.filter(a => {
      if (filtroLider   && String(a.liderID) !== filtroLider)   return false;
      if (filtroCartera && a.nombreCartera   !== filtroCartera) return false;
      if (termino       && !a.nombreAgente.toLowerCase().includes(termino)) return false;
      return true;
    });
  }, [data, filtroLider, filtroCartera, busqueda]);

  const bajoUmbralCount = umbral > 0 ? agentesFiltrados.filter(a => a.honorarios < umbral).length : 0;

  const agentesVisibles = useMemo(() => {
    if (!(soloBajoUmbral && umbral > 0)) return agentesFiltrados;
    return agentesFiltrados.filter(a => a.honorarios < umbral);
  }, [agentesFiltrados, soloBajoUmbral, umbral]);

  // Filas con cumplimiento calculado para poder ordenar por esa columna.
  // El estado de orden vive aquí (no en la tabla hija) para que la descarga lo respete.
  const rowsAgentes = useMemo(() => agentesVisibles.map(a => {
    const valorActual = a.tipoMeta === 'Honorarios' ? a.honorarios : a.recaudo;
    return {
      ...a,
      cumplimiento: a.valorMeta > 0 ? valorActual / a.valorMeta : 0,
      // Caída = dinero que falta para lo que debería llevar HOY (0 si ya lo alcanzó)
      caida: a.tieneMeta ? Math.max(0, a.deberiaLlevarHoy - valorActual) : 0,
    };
  }), [agentesVisibles]);
  const sortAgentes      = useSortable(rowsAgentes as unknown as Record<string, unknown>[]);
  const agentesOrdenados = sortAgentes.sorted as unknown as (InformeAgenteMes & { cumplimiento: number; caida: number })[];

  // Actualizar ref en cada render para que la descarga use el filtro Y el orden actual
  latestRef.current = { agentesVisibles: agentesOrdenados, esLider, soloBajoUmbral, umbral };

  useEffect(() => {
    if (!onRegisterDownload) return;
    const nombreMes = MESES[mes - 1];
    onRegisterDownload((): PdfExportDescriptor | null => {
      const { agentesVisibles, esLider, soloBajoUmbral, umbral } = latestRef.current;
      if (agentesVisibles.length === 0) return null;
      const esReporteUmbral = soloBajoUmbral && umbral > 0;

      const headCols = ['#', 'Agente', ...(esLider ? [] : ['Líder']), 'Cartera',
        'Gestiones', 'Recaudo', 'Honorarios', 'Tipo Meta',
        'Meta', 'Debería hoy', 'Caída', 'Prom./día', 'Cump.'];
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
          a.tieneMeta ? cop(a.caida) : '—',
          cop(a.promedioPagoDia),
          a.valorMeta > 0 ? valorActual / a.valorMeta : -1,
        ];
      });
      const offset = esLider ? 0 : 1;
      const colStyles: Record<number, { halign: 'left' | 'center' | 'right' }> = {
        0: { halign: 'center' },
        1: { halign: 'left' },
        [2 + offset]: { halign: 'left' },
        [3 + offset]: { halign: 'center' },
        [6 + offset]: { halign: 'center' },
      };
      // Columna "Líder" (índice 2) alineada a la izquierda solo cuando es visible
      if (!esLider) colStyles[2] = { halign: 'left' };
      const pdfTable: PdfTableData = { head: [headCols], body, cumpColIndex, colStyles };
      return {
        columnas: headCols,
        generar: (idx) => buildExportarPDF({
          tituloArchivo: esReporteUmbral
            ? `Asesores_Bajo_Honorarios_${nombreMes}_${año}`
            : `Informe_Agentes_${nombreMes}_${año}`,
          tituloPDF: esReporteUmbral
            ? `Asesores con menos de ${cop(umbral)} en Honorarios — ${nombreMes} ${año}`
            : `Informe Agentes — ${nombreMes} ${año}`,
          subtitulo:     `Generado el ${new Date().toLocaleDateString('es-CO', { day: 'numeric', month: 'long', year: 'numeric' })}`,
          pdfTable: filtrarColumnas(pdfTable, idx),
          // Sin gráfico: el informe de Agentes ya no incluye página de gráfica.
        }),
      };
    });
    return () => onRegisterDownload(null);
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

      {/* Filtros (integrados sobre la tabla) */}
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

      {/* Generador de informe gerencial: asesores bajo el honorario exigido */}
      <div style={{
        display: 'flex', gap: 12, flexWrap: 'wrap', alignItems: 'center',
        background: 'var(--z-bg-card)', border: '1px solid var(--z-border)',
        borderRadius: 10, padding: '12px 16px',
      }}>
        <span style={{ fontSize: 12, fontWeight: 600, color: 'var(--z-text-muted)', whiteSpace: 'nowrap' }}>
          Honorarios exigidos ($):
        </span>
        <input
          type="number" min="0" placeholder="Ej: 1500000"
          value={umbralTexto}
          onChange={e => setUmbralTexto(e.target.value)}
          style={{
            background: 'var(--z-bg-page)', border: '1px solid var(--z-border)',
            borderRadius: 6, padding: '5px 10px', fontSize: 13,
            color: 'var(--z-text-primary)', width: 160, outline: 'none',
          }}
        />
        <label style={{
          display: 'flex', alignItems: 'center', gap: 6, fontSize: 13,
          color: umbral > 0 ? 'var(--z-text-primary)' : 'var(--z-text-muted)',
          cursor: umbral > 0 ? 'pointer' : 'not-allowed',
        }}>
          <input
            type="checkbox"
            checked={soloBajoUmbral}
            disabled={umbral <= 0}
            onChange={e => setSoloBajoUmbral(e.target.checked)}
          />
          Ver solo asesores con menos de {umbral > 0 ? cop(umbral) : '$0'} en honorarios
        </label>
        {umbral > 0 && (
          <span style={{
            fontSize: 12, fontWeight: 600, color: C.rojo, marginLeft: 'auto',
            background: `${C.rojo}18`, padding: '4px 10px', borderRadius: 6, whiteSpace: 'nowrap',
          }}>
            {bajoUmbralCount} asesor{bajoUmbralCount !== 1 ? 'es' : ''} bajo el umbral
          </span>
        )}
      </div>

      {/* Tabla */}
      {agentesVisibles.length === 0 ? (
        <p style={{ textAlign: 'center', color: 'var(--z-text-muted)', fontSize: 13, padding: '32px 0' }}>
          Sin agentes para el filtro seleccionado.
        </p>
      ) : (
      <TablaAgentes
        agentes={agentesOrdenados} esLider={esLider}
        sortKey={sortAgentes.sortKey} sortDir={sortAgentes.sortDir} onSort={sortAgentes.handleSort}
      />
      )}
    </div>
  );
}

function TablaAgentes({ agentes, esLider, sortKey, sortDir, onSort }: {
  agentes: (InformeAgenteMes & { cumplimiento: number; caida: number })[]; esLider: boolean;
} & SortHandlers) {
  const th = (label: string, k: string, align?: 'left' | 'right' | 'center') => (
    <ThSort label={label} k={k} sortKey={sortKey} sortDir={sortDir} onSort={onSort} align={align} />
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
              {th('Gestiones',   'gestionado',      'center')}
              {th('Recaudo',     'recaudo')}
              {th('Honorarios',  'honorarios')}
              <th style={{ ...TH, textAlign: 'center' }}>Tipo Meta</th>
              {th('Meta',        'valorMeta')}
              {th('Debería hoy', 'deberiaLlevarHoy')}
              {th('Caída',       'caida')}
              {th('Prom./día',   'promedioPagoDia')}
              {th('Cump.',       'cumplimiento',    'center')}
            </tr>
          </thead>
          <tbody>
            {agentes.map((a, i) => <FilaAgente key={a.agenteID} a={a} pos={i + 1} esLider={esLider} />)}
          </tbody>
        </table>
      </div>
    </div>
  );
}

function FilaAgente({ a, pos, esLider }: { a: InformeAgenteMes & { caida: number }; pos: number; esLider: boolean }) {
  return (
    <tr
      onMouseEnter={(e) => { e.currentTarget.style.background = 'var(--z-bg-page)'; }}
      onMouseLeave={(e) => { e.currentTarget.style.background = 'transparent'; }}
    >
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
      <td style={{ ...TD, textAlign: 'right', color: a.caida > 0 ? C.rojo : C.verde }}>
        {a.tieneMeta ? cop(a.caida) : '—'}
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
  caida:            number;
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
          tieneMeta: f.tieneMeta, cumplimiento: 0, caida: 0,
        });
      }
    }
    return Array.from(porLider.values()).map(l => ({
      ...l,
      cumplimiento: l.valorMeta > 0 ? l.honorarios / l.valorMeta : 0,
      // Caída = dinero que falta para lo que debería llevar HOY (0 si ya lo alcanzó)
      caida: l.tieneMeta ? Math.max(0, l.deberiaLlevarHoy - (l.tipoMeta === 'Honorarios' ? l.honorarios : l.recaudo)) : 0,
    }));
  }, [data]);

  // Orden gobernado en el padre para que la descarga PDF use el mismo orden visible
  const sortLideres      = useSortable(lideres as unknown as Record<string, unknown>[]);
  const lideresOrdenados = sortLideres.sorted as unknown as LiderAgregado[];

  latestRef.current = { lideres: lideresOrdenados };

  useEffect(() => {
    if (!onRegisterDownload) return;
    const nombreMes = MESES[mes - 1];
    onRegisterDownload((): PdfExportDescriptor | null => {
      const { lideres } = latestRef.current;
      if (lideres.length === 0) return null;

      const headCols = ['Líder', 'Carteras', 'Asesores', 'Asignaciones',
        'Recaudo', 'Honorarios', 'Tipo Meta', 'Meta', 'Debería hoy', 'Caída', 'Proyección', 'Cump.'];
      const cumpColIndex = headCols.length - 1;
      const body = lideres.map(l => [
        l.nombreLider, l.carteras, l.asesores,
        l.capacidad.toLocaleString('es-CO'),
        cop(l.recaudo), cop(l.honorarios),
        l.tipoMeta ?? '—',
        l.tieneMeta ? cop(l.valorMeta) : '—',
        l.tieneMeta ? cop(l.deberiaLlevarHoy) : '—',
        l.tieneMeta ? cop(l.caida) : '—',
        cop(l.proyeccionCierre),
        l.valorMeta > 0 ? l.honorarios / l.valorMeta : -1,
      ]);
      const colStyles: Record<number, { halign: 'left' | 'center' | 'right' }> = {
        0: { halign: 'left' },
        1: { halign: 'center' }, 2: { halign: 'center' },
        3: { halign: 'center' }, 6: { halign: 'center' },
      };
      const pdfTable: PdfTableData = { head: [headCols], body, cumpColIndex, colStyles };
      return {
        columnas: headCols,
        generar: (idx) => buildExportarPDF({
          tituloArchivo: `Informe_Lideres_${nombreMes}_${año}`,
          tituloPDF:     `Informe Líderes — ${nombreMes} ${año}`,
          subtitulo:     `Generado el ${new Date().toLocaleDateString('es-CO', { day: 'numeric', month: 'long', year: 'numeric' })}`,
          pdfTable: filtrarColumnas(pdfTable, idx),
          refGrafico,
        }),
      };
    });
    return () => onRegisterDownload(null);
  }, [onRegisterDownload, año, mes]);

  if (isLoading) return <Cargando />;
  if (isError || !data) return <ErrorMsg />;

  const lideresConMeta = lideres.filter(l => l.tieneMeta);
  const avgCumplimiento = lideresConMeta.length > 0
    ? lideresConMeta.reduce((s, l) => s + l.cumplimiento, 0) / lideresConMeta.length
    : null;

  const totalHonorarios = lideres.reduce((s, l) => s + l.honorarios, 0);
  // Meta desglosada por tipo. Si conviven metas de Recaudo y Honorarios en el mes,
  // el chip "Total Meta" muestra ambos valores por separado (no son comparables al sumarse).
  const metaRecaudo    = data.liderCarteras.filter(f => f.tipoMeta === 'Recaudo').reduce((s, f) => s + f.valorMeta, 0);
  const metaHonorarios = data.liderCarteras.filter(f => f.tipoMeta === 'Honorarios').reduce((s, f) => s + f.valorMeta, 0);
  const metaMixta = metaRecaudo > 0 && metaHonorarios > 0;

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
        <KpiCard label="Total Recaudo"    value={cop(data.totalRecaudo)} color={C.brand} />
        <KpiCard label="Total Honorarios" value={cop(totalHonorarios)}   color={C.honorarios} />
        <KpiCard
          label="Total Meta"
          color={C.meta}
          value={metaMixta ? (
            <span style={{ display: 'flex', flexDirection: 'column', gap: 2, fontSize: 15 }}>
              <span><span style={{ fontSize: 11, fontWeight: 400, color: 'var(--z-text-muted)' }}>Recaudo: </span>{cop(metaRecaudo)}</span>
              <span><span style={{ fontSize: 11, fontWeight: 400, color: 'var(--z-text-muted)' }}>Honorarios: </span>{cop(metaHonorarios)}</span>
            </span>
          ) : cop(data.totalMeta)}
        />
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
      <TablaLideres
        lideres={lideresOrdenados}
        sortKey={sortLideres.sortKey} sortDir={sortLideres.sortDir} onSort={sortLideres.handleSort}
      />
    </div>
  );
}

function TablaLideres({ lideres, sortKey, sortDir, onSort }: {
  lideres: LiderAgregado[];
} & SortHandlers) {
  const th = (label: string, k: string, align?: 'left' | 'right' | 'center') => (
    <ThSort label={label} k={k} sortKey={sortKey} sortDir={sortDir} onSort={onSort} align={align} />
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
              {th('Caída',             'caida')}
              {th('Proyección cierre', 'proyeccionCierre')}
              {th('Cump.',             'cumplimiento',    'center')}
            </tr>
          </thead>
          <tbody>
            {lideres.map(l => {
              const proyOk = l.proyeccionCierre >= l.valorMeta && l.valorMeta > 0;
              return (
                <tr
                  key={l.liderID}
                  onMouseEnter={(e) => { e.currentTarget.style.background = 'var(--z-bg-page)'; }}
                  onMouseLeave={(e) => { e.currentTarget.style.background = 'transparent'; }}
                >
                  <td style={{ ...TD, textAlign: 'left', fontWeight: 500, color: 'var(--z-text-primary)' }}>{l.nombreLider}</td>
                  <td style={{ ...TD, textAlign: 'center', color: 'var(--z-text-muted)' }}>{l.carteras}</td>
                  <td style={{ ...TD, textAlign: 'center' }}>{l.asesores}</td>
                  <td style={{ ...TD, textAlign: 'center' }}>{l.capacidad.toLocaleString('es-CO')}</td>
                  <td style={{ ...TD, textAlign: 'right', color: C.brand, fontWeight: 600 }}>{cop(l.recaudo)}</td>
                  <td style={{ ...TD, textAlign: 'right', color: C.honorarios }}>{cop(l.honorarios)}</td>
                  <td style={{ ...TD, textAlign: 'center' }}><ChipTipoMeta tipo={l.tipoMeta} /></td>
                  <td style={{ ...TD, textAlign: 'right', color: 'var(--z-text-muted)' }}>{l.tieneMeta ? cop(l.valorMeta) : '—'}</td>
                  <td style={{ ...TD, textAlign: 'right', color: 'var(--z-text-muted)' }}>{l.tieneMeta ? cop(l.deberiaLlevarHoy) : '—'}</td>
                  <td style={{ ...TD, textAlign: 'right', color: l.caida > 0 ? C.rojo : C.verde }}>{l.tieneMeta ? cop(l.caida) : '—'}</td>
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

const COSTO_ASESOR = 4_700_000;

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
  caida:            number;
}

// Enriquece una fila líder-cartera del backend con los campos derivados de la tabla.
// Se usa tanto para las filas normales como para el desglose de BASE GENERAL.
function aFilaLC(f: InformeLiderCartera): InformeLiderCartera & { cumplimiento: number; centroCostos: number; caida: number } {
  return {
    ...f,
    cumplimiento: f.valorMeta > 0 ? f.honorarios / f.valorMeta : 0,
    centroCostos: f.totalAsesores * COSTO_ASESOR,
    // Caída = dinero que falta para lo que debería llevar HOY (0 si ya lo alcanzó)
    caida: f.tieneMeta ? Math.max(0, f.deberiaLlevarHoy - (f.tipoMeta === 'Honorarios' ? f.honorarios : f.recaudo)) : 0,
  };
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
  const [filtroCartera, setFiltroCartera] = useState('');
  const refGrafico = useRef<HTMLDivElement>(null);
  type FilaLC = InformeLiderCartera & { cumplimiento: number; centroCostos: number; caida: number };
  const latestRef  = useRef<{ filasPorLiderCartera: FilaLC[]; filasCartera: CarteraAgregada[]; vista: VistaCarteras; esLider: boolean }>({ filasPorLiderCartera: [], filasCartera: [], vista: 'lider-cartera', esLider: false });

  const { data, isLoading, isError } = useQuery({
    queryKey: ['inf-lideres', año, mes],
    queryFn:  () => getInformeLideres(año, mes),
  });

  // Carteras cuyo líder asignado es "BASE GENERAL" → habilitan el desglose especial por asesor.
  const bgCarteraIDs = useMemo(() => {
    const s = new Set<number>();
    data?.liderCarteras.forEach(f => {
      if (f.liderID === 25 || f.nombreLider === 'BASE GENERAL') s.add(f.carteraID);
    });
    return s;
  }, [data]);

  // Solo en vista líder-cartera y cuando la cartera seleccionada es de BASE GENERAL.
  const esBaseGeneralSel = vista === 'lider-cartera' && !!filtroCartera && bgCarteraIDs.has(Number(filtroCartera));

  const bgQuery = useQuery({
    queryKey: ['inf-cartera-bg', Number(filtroCartera), año, mes],
    queryFn:  () => getInformeCarteraBaseGeneral(Number(filtroCartera), año, mes),
    enabled:  esBaseGeneralSel,
  });

  const { optsLider, optsCartera, filasPorLiderCarteraNormal, filasCartera } = useMemo(() => {
    if (!data) return { optsLider: [], optsCartera: [], filasPorLiderCarteraNormal: [], filasCartera: [] };

    const optsLider = Array.from(
      new Map(data.liderCarteras.map(f => [f.liderID, f.nombreLider])).entries()
    ).map(([value, label]) => ({ value: String(value), label }));

    // Carteras disponibles, restringidas al líder filtrado (igual patrón que el tab Agentes)
    const optsCartera = Array.from(
      new Map(
        data.liderCarteras
          .filter(f => !filtroLider || String(f.liderID) === filtroLider)
          .map(f => [f.carteraID, f.nombreCartera])
      ).entries()
    ).map(([value, label]) => ({ value: String(value), label }));

    const fuenteFiltrada = data.liderCarteras.filter(f =>
      (!filtroLider   || String(f.liderID)   === filtroLider) &&
      (!filtroCartera || String(f.carteraID) === filtroCartera));

    // ── Vista Líder-Cartera ────────────────────────────────────────────────────
    const filasPorLiderCarteraNormal = fuenteFiltrada.map(aFilaLC);

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
          tieneMeta: f.tieneMeta, cumplimiento: 0, caida: 0,
          centroCostos: f.totalAsesores * COSTO_ASESOR,
        });
      }
    }
    const filasCartera: CarteraAgregada[] = Array.from(porCartera.values()).map(c => ({
      ...c,
      cumplimiento: c.valorMeta > 0 ? c.honorarios / c.valorMeta : 0,
      // Caída = dinero que falta para lo que debería llevar HOY (0 si ya lo alcanzó)
      caida: c.tieneMeta ? Math.max(0, c.deberiaLlevarHoy - (c.tipoMeta === 'Honorarios' ? c.honorarios : c.recaudo)) : 0,
    }));

    return { optsLider, optsCartera, filasPorLiderCarteraNormal, filasCartera };
  }, [data, filtroLider, filtroCartera]);

  // Filas efectivas de la vista líder-cartera: desglose por asesor si la cartera es BASE GENERAL,
  // de lo contrario el filtrado normal por líder/cartera.
  const filasPorLiderCartera = useMemo(
    () => esBaseGeneralSel ? (bgQuery.data?.liderCarteras ?? []).map(aFilaLC) : filasPorLiderCarteraNormal,
    [esBaseGeneralSel, bgQuery.data, filasPorLiderCarteraNormal],
  );

  // Una instancia de orden por vista; el padre conserva el orden para que la descarga lo respete
  const sortLC   = useSortable(filasPorLiderCartera as unknown as Record<string, unknown>[]);
  const sortCart = useSortable(filasCartera         as unknown as Record<string, unknown>[]);
  const filasLCOrdenadas   = sortLC.sorted   as unknown as (InformeLiderCartera & { cumplimiento: number; centroCostos: number; caida: number })[];
  const filasCartOrdenadas = sortCart.sorted as unknown as CarteraAgregada[];

  latestRef.current = { filasPorLiderCartera: filasLCOrdenadas, filasCartera: filasCartOrdenadas, vista, esLider };

  useEffect(() => {
    if (!onRegisterDownload) return;
    const nombreMes = MESES[mes - 1];
    onRegisterDownload((): PdfExportDescriptor | null => {
      const { filasPorLiderCartera, filasCartera, vista, esLider } = latestRef.current;
      const filas = vista === 'lider-cartera' ? filasPorLiderCartera : filasCartera;
      if (filas.length === 0) return null;

      let headCols: string[];
      let body: (string | number | null)[][];

      if (vista === 'lider-cartera') {
        headCols = [...(esLider ? [] : ['Líder']), 'Cartera', 'Asesores',
          'Centro Costos', 'Asignaciones', 'Recaudo', 'Honorarios',
          'Tipo Meta', 'Meta', 'Debería hoy', 'Caída', 'Proyección', 'Cump.'];
        body = filasPorLiderCartera.map(f => [
          ...(esLider ? [] : [f.nombreLider]),
          f.nombreCartera, f.totalAsesores,
          cop(f.centroCostos), f.capacidad.toLocaleString('es-CO'),
          cop(f.recaudo), cop(f.honorarios),
          f.tipoMeta ?? '—',
          f.tieneMeta ? cop(f.valorMeta) : '—',
          f.tieneMeta ? cop(f.deberiaLlevarHoy) : '—',
          f.tieneMeta ? cop(f.caida) : '—',
          cop(f.proyeccionCierre),
          f.valorMeta > 0 ? f.honorarios / f.valorMeta : -1,
        ]);
      } else {
        headCols = ['Cartera', 'Líderes', 'Asesores', 'Centro Costos', 'Asignaciones',
          'Recaudo', 'Honorarios', 'Tipo Meta', 'Meta', 'Debería hoy', 'Caída', 'Proyección', 'Cump.'];
        body = filasCartera.map(c => [
          c.nombreCartera, c.lideres, c.asesores,
          cop(c.centroCostos), c.capacidad.toLocaleString('es-CO'),
          cop(c.recaudo), cop(c.honorarios),
          c.tipoMeta ?? '—',
          c.tieneMeta ? cop(c.valorMeta) : '—',
          c.tieneMeta ? cop(c.deberiaLlevarHoy) : '—',
          c.tieneMeta ? cop(c.caida) : '—',
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
      const pdfTable: PdfTableData = { head: [headCols], body, cumpColIndex, colStyles };
      return {
        columnas: headCols,
        generar: (idx) => buildExportarPDF({
          tituloArchivo: `Informe_Carteras_${nombreMes}_${año}`,
          tituloPDF:     `Informe Carteras — ${nombreMes} ${año}`,
          subtitulo:     `Generado el ${new Date().toLocaleDateString('es-CO', { day: 'numeric', month: 'long', year: 'numeric' })}`,
          pdfTable: filtrarColumnas(pdfTable, idx),
          refGrafico,
        }),
      };
    });
    return () => onRegisterDownload(null);
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
        <KpiCard label="Carteras Únicas"  value={String(filasCartera.length)} />
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
                onChange={v => { setFiltroLider(v); setFiltroCartera(''); }}
                options={optsLider}
                placeholder="Todos los líderes"
              />
            )}

            {vista === 'lider-cartera' && (
              <FiltroSelect
                label="Cartera:"
                value={filtroCartera}
                onChange={setFiltroCartera}
                options={optsCartera}
                placeholder="Todas las carteras"
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
        ? <TablaCarterasLiderCartera filas={filasLCOrdenadas} esLider={esLider}
            sortKey={sortLC.sortKey} sortDir={sortLC.sortDir} onSort={sortLC.handleSort} />
        : <TablaCarterasAgrupada     filas={filasCartOrdenadas}
            sortKey={sortCart.sortKey} sortDir={sortCart.sortDir} onSort={sortCart.handleSort} />
      }
    </div>
  );
}

function TablaCarterasLiderCartera({ filas, esLider, sortKey, sortDir, onSort }: {
  filas: (InformeLiderCartera & { cumplimiento: number; centroCostos: number; caida: number })[]; esLider: boolean;
} & SortHandlers) {
  const th = (label: string, k: string, align?: 'left' | 'right' | 'center') => (
    <ThSort label={label} k={k} sortKey={sortKey} sortDir={sortDir} onSort={onSort} align={align} />
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
              {th('Caída',             'caida')}
              {th('Proyección',        'proyeccionCierre')}
              {th('Cump.',             'cumplimiento',    'center')}
            </tr>
          </thead>
          <tbody>
            {filas.map(f => (
              <tr
                key={`${f.liderID}-${f.carteraID}`}
                onMouseEnter={(e) => { e.currentTarget.style.background = 'var(--z-bg-page)'; }}
                onMouseLeave={(e) => { e.currentTarget.style.background = 'transparent'; }}
              >
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
                <td style={{ ...TD, textAlign: 'right', color: f.caida > 0 ? C.rojo : C.verde }}>{f.tieneMeta ? cop(f.caida) : '—'}</td>
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

function TablaCarterasAgrupada({ filas, sortKey, sortDir, onSort }: {
  filas: CarteraAgregada[];
} & SortHandlers) {
  const th = (label: string, k: string, align?: 'left' | 'right' | 'center') => (
    <ThSort label={label} k={k} sortKey={sortKey} sortDir={sortDir} onSort={onSort} align={align} />
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
              {th('Caída',             'caida')}
              {th('Proyección',        'proyeccionCierre')}
              {th('Cump.',             'cumplimiento',    'center')}
            </tr>
          </thead>
          <tbody>
            {filas.map(c => (
              <tr
                key={c.carteraID}
                onMouseEnter={(e) => { e.currentTarget.style.background = 'var(--z-bg-page)'; }}
                onMouseLeave={(e) => { e.currentTarget.style.background = 'transparent'; }}
              >
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
                <td style={{ ...TD, textAlign: 'right', color: c.caida > 0 ? C.rojo : C.verde }}>{c.tieneMeta ? cop(c.caida) : '—'}</td>
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

  // Orden gobernado en el padre para que la descarga PDF use el mismo orden visible
  const sortComp       = useSortable(filas as unknown as Record<string, unknown>[]);
  const filasOrdenadas = sortComp.sorted as unknown as FilaComparativo[];

  latestRef.current = { filas: filasOrdenadas, labelA, labelB, labelC, numPeriodos };

  useEffect(() => {
    if (!onRegisterDownload) return;
    onRegisterDownload((): PdfExportDescriptor | null => {
      const { filas, labelA, labelB, labelC, numPeriodos } = latestRef.current;
      if (filas.length === 0) return null;

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
      const pdfTable: PdfTableData = { head: [headCols], body, cumpColIndex: -1, colStyles };
      return {
        columnas: headCols,
        generar: (idx) => buildExportarPDF({
          tituloArchivo: `Informe_Comparativo_${tituloBase}`,
          tituloPDF:     `Informe Comparativo — ${partes.join(' vs ')}`,
          subtitulo:     `Generado el ${new Date().toLocaleDateString('es-CO', { day: 'numeric', month: 'long', year: 'numeric' })}`,
          pdfTable: filtrarColumnas(pdfTable, idx),
          refGrafico,
        }),
      };
    });
    return () => onRegisterDownload(null);
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

          <TablaComparativo
            filas={filasOrdenadas} labelA={labelA} labelB={labelB} labelC={numPeriodos === 3 ? labelC : null}
            sortKey={sortComp.sortKey} sortDir={sortComp.sortDir} onSort={sortComp.handleSort}
          />
        </>
      )}
    </div>
  );
}

function TablaComparativo({ filas, labelA, labelB, labelC, sortKey, sortDir, onSort }: {
  filas: FilaComparativo[]; labelA: string; labelB: string; labelC: string | null;
} & SortHandlers) {
  const th = (label: string, k: string, align?: 'left' | 'right' | 'center') => (
    <ThSort label={label} k={k} sortKey={sortKey} sortDir={sortDir} onSort={onSort} align={align} />
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
            {filas.map(f => {
              const varAB = f.valA > 0 ? ((f.valB - f.valA) / f.valA * 100) : null;
              const varBC = f.valB > 0 ? ((f.valC - f.valB) / f.valB * 100) : null;
              return (
                <tr
                  key={f.carteraID}
                  onMouseEnter={(e) => { e.currentTarget.style.background = 'var(--z-bg-page)'; }}
                  onMouseLeave={(e) => { e.currentTarget.style.background = 'transparent'; }}
                >
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
  { id: 'lideres',     label: 'Líderes'     },
  { id: 'carteras',    label: 'Carteras'    },
  { id: 'agentes',     label: 'Agentes'     },
  { id: 'comparativo', label: 'Comparativo' },
];

export function InformesRecaudoPage() {
  const { usuario } = useAuth();
  const esLider = usuario?.rol === 'Líder';

  // Tab inicial: los líderes solo ven "Agentes"; el resto arranca en "Líderes".
  const [tabActiva, setTabActiva] = useState<Tab>(() => esLider ? 'agentes' : 'lideres');
  const [año, setAño] = useState(AÑO_ACTUAL);
  const [mes, setMes] = useState(MES_ACTUAL);
  const [descargando, setDescargando] = useState(false);
  const [modalCols, setModalCols] = useState<PdfExportDescriptor | null>(null);
  const downloadFnRef = useRef<DownloadFactory | null>(null);

  // El registro/limpieza lo gobierna cada tab vía el cleanup de su efecto
  // (montar = registrar, desmontar = null), evitando el clobber por orden de efectos.
  const handleRegisterDownload = useCallback<DownloadRegistrar>((fn) => {
    downloadFnRef.current = fn;
  }, []);

  // Al hacer clic en "Descargar Informe": evaluar la factory del tab activo para
  // conocer las columnas disponibles y abrir el modal de selección de columnas.
  const handleDescargar = () => {
    if (!downloadFnRef.current || descargando) return;
    const descriptor = downloadFnRef.current();
    if (!descriptor) return;            // sin filas que exportar
    setModalCols(descriptor);
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
              background: descargando ? '#94a3b8' : C.naranja,
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
      {/* Subrayado animado: crece desde el centro al hover; el tab activo queda subrayado completo. */}
      <style>{`
        .z-tab { position: relative; transition: color 0.2s ease; }
        .z-tab::after {
          content: '';
          position: absolute;
          left: 50%; right: 50%; bottom: -1px; height: 2px;
          background: var(--z-brand);
          transition: left 0.22s ease, right 0.22s ease;
        }
        .z-tab:hover { color: var(--z-brand); }
        .z-tab:hover::after { left: 14px; right: 14px; }
        .z-tab-activa::after,
        .z-tab-activa:hover::after { left: 0; right: 0; }
      `}</style>
      <div style={{ display: 'flex', gap: 2, borderBottom: '1px solid var(--z-border)' }}>
        {tabsVisibles.map(t => (
          <button
            key={t.id}
            onClick={() => setTabActiva(t.id)}
            className={`z-tab${tabActiva === t.id ? ' z-tab-activa' : ''}`}
            style={{
              padding: '9px 20px',
              fontSize: 13,
              fontWeight: tabActiva === t.id ? 600 : 400,
              color: tabActiva === t.id ? 'var(--z-brand)' : 'var(--z-text-muted)',
              background: 'none',
              border: 'none',
              cursor: 'pointer',
              marginBottom: -1,
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

      {/* Modal de selección de columnas para el PDF */}
      {modalCols && (
        <ModalColumnas
          columnas={modalCols.columnas}
          generando={descargando}
          onConfirm={async (idx) => {
            setDescargando(true);
            try { await modalCols.generar(idx); }
            finally { setDescargando(false); setModalCols(null); }
          }}
          onClose={() => setModalCols(null)}
        />
      )}
    </div>
  );
}
