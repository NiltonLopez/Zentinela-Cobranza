import { useState, useMemo, useRef } from 'react';
import { useQuery } from '@tanstack/react-query';
import {
  ResponsiveContainer,
  BarChart,
  Bar,
  XAxis,
  YAxis,
  Tooltip,
  CartesianGrid,
  ReferenceLine,
} from 'recharts';
import { getInformeAgenciasExternas } from '../../api/informesRecaudoApi';
import type { FilaAgenciaExterna } from '../../types/informesRecaudo';
import {
  C, cop, MESES, KpiCard, Semaforo, ChipTipoMeta, Cargando, ErrorMsg,
  useSortable, ThSort, TD, TooltipCOP, LeyendaGrafico,
  buildExportarPDF, filtrarColumnas, ModalColumnas,
} from '../../components/informes/reporteCompartido';
import type {
  PdfTableData, PdfExportDescriptor, SortHandlers,
} from '../../components/informes/reporteCompartido';

// ── Constantes ────────────────────────────────────────────────────────────────
const hoy        = new Date();
const AÑO_ACTUAL = hoy.getFullYear();
const MES_ACTUAL = hoy.getMonth() + 1;

// Centro de costos por asesor (mismo valor que el informe líder-cartera).
const COSTO_ASESOR = 4_700_000;

// Fila con métricas derivadas (espeja el informe líder-cartera).
interface FilaExterna extends FilaAgenciaExterna {
  centroCostos: number;
  cumplimiento: number;
  caida:        number;
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

// ── Tabla de detalle (espejo líder-cartera) ───────────────────────────────────
function TablaExternas({ filas, sortKey, sortDir, onSort }: {
  filas: FilaExterna[];
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
              {th('Líder',        'nombreLider',      'left')}
              {th('Cartera',      'nombreCartera',    'left')}
              {th('Asesores',     'totalAsesores',    'center')}
              {th('Centro Costos','centroCostos',     'right')}
              {th('Asignaciones', 'totalAsignaciones','center')}
              {th('Recaudo',      'recaudo',          'right')}
              {th('Honorarios',   'honorarios',       'right')}
              {th('Tipo Meta',    'tipoMeta',         'center')}
              {th('Meta',         'valorMeta',        'right')}
              {th('Debería hoy',  'deberiaLlevarHoy', 'right')}
              {th('Caída',        'caida',            'right')}
              {th('Proyección',   'proyeccionCierre', 'right')}
              {th('Cump.',        'cumplimiento',     'center')}
            </tr>
          </thead>
          <tbody>
            {filas.map(f => (
              <tr key={`${f.liderID}-${f.carteraID}`}>
                <td style={{ ...TD }}>{f.nombreLider}</td>
                <td style={{ ...TD }}>{f.nombreCartera}</td>
                <td style={{ ...TD, textAlign: 'center' }}>{f.totalAsesores}</td>
                <td style={{ ...TD, textAlign: 'right' }}>{cop(f.centroCostos)}</td>
                <td style={{ ...TD, textAlign: 'center' }}>{f.totalAsignaciones.toLocaleString('es-CO')}</td>
                <td style={{ ...TD, textAlign: 'right' }}>{cop(f.recaudo)}</td>
                <td style={{ ...TD, textAlign: 'right' }}>{cop(f.honorarios)}</td>
                <td style={{ ...TD, textAlign: 'center' }}><ChipTipoMeta tipo={f.tipoMeta} /></td>
                <td style={{ ...TD, textAlign: 'right' }}>{f.tieneMeta ? cop(f.valorMeta) : '—'}</td>
                <td style={{ ...TD, textAlign: 'right' }}>{f.tieneMeta ? cop(f.deberiaLlevarHoy) : '—'}</td>
                <td style={{ ...TD, textAlign: 'right', color: f.caida > 0 ? C.rojo : undefined }}>
                  {f.tieneMeta ? cop(f.caida) : '—'}
                </td>
                <td style={{ ...TD, textAlign: 'right', color: f.tieneMeta && f.proyeccionCierre >= f.valorMeta ? C.verde : undefined }}>
                  {cop(f.proyeccionCierre)}
                </td>
                <td style={{ ...TD, textAlign: 'center' }}>
                  {f.tieneMeta ? <Semaforo valorActual={f.honorarios} valorMeta={f.valorMeta} /> : '—'}
                </td>
              </tr>
            ))}
          </tbody>
        </table>
      </div>
    </div>
  );
}

// ── Página ────────────────────────────────────────────────────────────────────
export function AgenciasExternasPage() {
  const [año, setAño] = useState(AÑO_ACTUAL);
  const [mes, setMes] = useState(MES_ACTUAL);
  const [descargando, setDescargando] = useState(false);
  const [modalCols, setModalCols]     = useState<PdfExportDescriptor | null>(null);
  const refGrafico = useRef<HTMLDivElement>(null);
  const [metricaC,  setMetricaC]  = useState<'honorarios' | 'recaudo'>('honorarios');
  const [criterioC, setCriterioC] = useState<'valor' | 'proyeccion' | 'meta'>('valor');

  const { data, isLoading, isError } = useQuery({
    queryKey: ['agencias-externas', año, mes],
    queryFn:  () => getInformeAgenciasExternas(año, mes),
  });

  // Filas con métricas derivadas (mismas fórmulas que el informe líder-cartera).
  const filas = useMemo<FilaExterna[]>(() => {
    return (data?.filas ?? []).map(f => ({
      ...f,
      centroCostos: f.totalAsesores * COSTO_ASESOR,
      cumplimiento: f.valorMeta > 0 ? f.honorarios / f.valorMeta : 0,
      caida: f.tieneMeta
        ? Math.max(0, f.deberiaLlevarHoy - (f.tipoMeta === 'Honorarios' ? f.honorarios : f.recaudo))
        : 0,
    }));
  }, [data]);

  // El estado de orden vive aquí para que la descarga PDF respete el orden visible.
  const sort  = useSortable(filas as unknown as Record<string, unknown>[]);
  const filasOrdenadas = sort.sorted as unknown as FilaExterna[];

  // Meta desglosada por tipo (igual que Líderes): si hay ambos tipos, se separa.
  const metaRecaudo    = filas.filter(f => f.tipoMeta === 'Recaudo').reduce((s, f) => s + f.valorMeta, 0);
  const metaHonorarios = filas.filter(f => f.tipoMeta === 'Honorarios').reduce((s, f) => s + f.valorMeta, 0);
  const metaMixta      = metaRecaudo > 0 && metaHonorarios > 0;

  // Gráfico agrupado (mismo que Informes de Recaudo → Tab Carteras):
  // por cada (líder/cartera) tres barras — métrica · debería hoy · meta.
  const campoOrden = criterioC === 'valor' ? metricaC : criterioC === 'proyeccion' ? 'proyeccionCierre' : 'valorMeta';

  const chartData = [...filas]
    .sort((a, b) => (b as unknown as Record<string, number>)[campoOrden] - (a as unknown as Record<string, number>)[campoOrden])
    .map(f => ({
      nombre:           `${f.nombreLider} / ${f.nombreCartera}`,
      metricaValor:     metricaC === 'honorarios' ? f.honorarios : f.recaudo,
      deberiaLlevarHoy: f.deberiaLlevarHoy,
      valorMeta:        f.valorMeta,
    }));

  const promDeberiaHoy = filas.length > 0
    ? filas.reduce((s, f) => s + f.deberiaLlevarHoy, 0) / filas.length
    : 0;

  // ── Descarga PDF con modal de columnas ──────────────────────────────────────
  const construirDescriptor = (): PdfExportDescriptor | null => {
    if (filasOrdenadas.length === 0) return null;
    const nombreMes = MESES[mes - 1];
    const headCols = ['Líder', 'Cartera', 'Asesores', 'Centro Costos', 'Asignaciones',
      'Recaudo', 'Honorarios', 'Tipo Meta', 'Meta', 'Debería hoy', 'Caída', 'Proyección', 'Cump.'];
    const cumpColIndex = headCols.length - 1;
    const body = filasOrdenadas.map(f => [
      f.nombreLider, f.nombreCartera, f.totalAsesores,
      cop(f.centroCostos), f.totalAsignaciones.toLocaleString('es-CO'),
      cop(f.recaudo), cop(f.honorarios),
      f.tipoMeta ?? '—',
      f.tieneMeta ? cop(f.valorMeta) : '—',
      f.tieneMeta ? cop(f.deberiaLlevarHoy) : '—',
      f.tieneMeta ? cop(f.caida) : '—',
      cop(f.proyeccionCierre),
      f.valorMeta > 0 ? f.honorarios / f.valorMeta : -1,
    ]);
    const colStyles: Record<number, { halign: 'left' | 'right' | 'center' }> = {
      0: { halign: 'left' }, 1: { halign: 'left' },
      2: { halign: 'center' }, 7: { halign: 'center' },
    };
    const pdfTable: PdfTableData = { head: [headCols], body, cumpColIndex, colStyles };
    return {
      columnas: headCols,
      generar: (idx) => buildExportarPDF({
        tituloArchivo: `Informe_Agencias_Externas_${nombreMes}_${año}`,
        tituloPDF:     `Agencias Externas — ${nombreMes} ${año}`,
        subtitulo:     `Generado el ${new Date().toLocaleDateString('es-CO', { day: 'numeric', month: 'long', year: 'numeric' })}`,
        pdfTable: filtrarColumnas(pdfTable, idx),
        refGrafico,
      }),
    };
  };

  const handleDescargar = () => {
    if (descargando) return;
    const d = construirDescriptor();
    if (!d) return;
    setModalCols(d);
  };

  return (
    <div style={{ display: 'flex', flexDirection: 'column', gap: 20, maxWidth: 1300, margin: '0 auto' }}>

      {/* Encabezado */}
      <div style={{ display: 'flex', alignItems: 'flex-start', justifyContent: 'space-between', flexWrap: 'wrap', gap: 12 }}>
        <div>
          <h1 style={{ fontSize: 20, fontWeight: 600, color: 'var(--z-text-primary)', margin: 0 }}>
            Agencias Externas
          </h1>
          <p style={{ fontSize: 13, color: 'var(--z-text-muted)', margin: '4px 0 0' }}>
            Recaudo, honorarios y cumplimiento de metas por líder externo en {MESES[mes - 1]} {año}.
          </p>
        </div>
        <div style={{ display: 'flex', alignItems: 'center', gap: 10, flexWrap: 'wrap' }}>
          <SelectorPeriodo año={año} mes={mes} onAño={setAño} onMes={setMes} />
          <button
            onClick={handleDescargar}
            disabled={descargando}
            style={{
              display: 'flex', alignItems: 'center', gap: 6,
              padding: '6px 14px', borderRadius: 7, fontSize: 13, fontWeight: 500,
              background: descargando ? '#94a3b8' : C.naranja, color: '#fff', border: 'none',
              cursor: descargando ? 'not-allowed' : 'pointer', opacity: descargando ? 0.7 : 1,
              whiteSpace: 'nowrap',
            }}
          >
            <span style={{ fontSize: 14 }}>{descargando ? '⏳' : '↓'}</span>
            {descargando ? 'Generando…' : 'Descargar Informe'}
          </button>
        </div>
      </div>

      {isLoading && <Cargando />}
      {isError   && <ErrorMsg />}

      {data && data.filas.length === 0 && (
        <div style={{
          background: 'var(--z-bg-card)', border: '1px solid var(--z-border)',
          borderRadius: 8, padding: 24, textAlign: 'center',
          fontSize: 14, color: 'var(--z-text-muted)',
        }}>
          No hay carteras externas registradas en el sistema.
        </div>
      )}

      {data && data.filas.length > 0 && (
        <>
          {/* KPIs (espejo del informe líder-cartera) */}
          <div style={{ display: 'flex', gap: 12, flexWrap: 'wrap' }}>
            <KpiCard label="Total Recaudo"    value={cop(data.totalRecaudo)}    color={C.brand} />
            <KpiCard label="Total Honorarios" value={cop(data.totalHonorarios)} color={C.honorarios} />
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
            <KpiCard label="Agencias activas" value={String(data.totalAgencias)} sub="líderes con cartera externa" />
          </div>

          {/* Gráfico agrupado — mismo formato que Informes de Recaudo → Tab Carteras */}
          <div style={{ background: 'var(--z-bg-card)', border: '1px solid var(--z-border)', borderRadius: 10, padding: '20px 16px' }}>
            <div style={{ display: 'flex', alignItems: 'center', justifyContent: 'space-between', flexWrap: 'wrap', gap: 12, marginBottom: 16 }}>
              <LeyendaGrafico items={[
                { color: metricaC === 'honorarios' ? C.honorarios : C.brand, label: metricaC === 'honorarios' ? 'Honorarios' : 'Recaudo' },
                { color: C.proyeccion, label: 'Debería hoy' },
                { color: C.meta,       label: 'Meta' },
              ]} />

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

          {/* Tabla detalle con métricas completas */}
          <TablaExternas
            filas={filasOrdenadas}
            sortKey={sort.sortKey} sortDir={sort.sortDir} onSort={sort.handleSort}
          />
        </>
      )}

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
