import { useEffect, useMemo, useRef } from 'react';
import { useQuery } from '@tanstack/react-query';
import { ResponsiveContainer, BarChart, Bar, XAxis, YAxis, CartesianGrid, Tooltip } from 'recharts';
import { obtenerResumenLocalizacion, problemaLocalizacion } from '../../../api/informesLocalizacionApi';
import type { FuenteResumen, ResumenLocalizacion } from '../../../types/informesLocalizacion';
import { COLOR_ESTADO_FUENTE, type EstadoFuente } from '../../../types/fuentesLocalizacion';
import {
  KpiCard, Cargando, ErrorMsg, LeyendaGrafico, buildExportarPDF, filtrarColumnas, cop,
} from '../../../components/informes/reporteCompartido';
import type { DownloadRegistrar, PdfExportDescriptor, PdfTableData } from '../../../components/informes/reporteCompartido';
import {
  AvisoNoMedible, COLOR_LOC, FiltrosAlcance, Tarjeta, colorNivel, describirAlcance, fmtFecha, fmtNum, fmtPct,
  type Alcance,
} from './compartidos';
import { PanelAlertasSemana } from './PanelAlertasSemana';

// ── Tick del eje Y: nombre de la fuente con el color de su estado ─────────────
interface TickProps { x?: number; y?: number; payload?: { value: string }; estados: Record<string, EstadoFuente> }

// recharts clona el elemento del tick e inyecta x, y y payload; `estados` llega como prop.
function TickFuente({ x = 0, y = 0, payload, estados }: TickProps) {
  const etiqueta = payload?.value ?? '';
  const color = COLOR_ESTADO_FUENTE[estados[etiqueta] ?? 'Inactivo'];
  return (
    <g transform={`translate(${x},${y})`}>
      <circle cx={-6} cy={0} r={4} fill={color} />
      <text x={-14} y={0} dy={4} textAnchor="end" fontSize={11} fill="var(--z-text-primary)">{etiqueta}</text>
    </g>
  );
}

function TooltipFuente({ active, payload }: { active?: boolean; payload?: { payload: FuenteResumen & { etiquetaEje: string } }[] }) {
  if (!active || !payload?.length) return null;
  const f = payload[0].payload;
  return (
    <div style={{ background: 'var(--z-bg-card)', border: '1px solid var(--z-border)', borderRadius: 8, padding: '10px 14px', fontSize: 12 }}>
      <p style={{ fontWeight: 600, margin: '0 0 4px', color: 'var(--z-text-primary)' }}>
        {f.etiqueta} <span style={{ color: COLOR_ESTADO_FUENTE[f.estado], fontWeight: 500 }}>({f.estado})</span>
      </p>
      <p style={{ margin: '2px 0', color: COLOR_LOC.encontrado }}>Encontrado: <strong>{fmtNum(f.encontrados)}</strong> ({fmtPct(f.pctEncontrados)})</p>
      <p style={{ margin: '2px 0', color: '#B45309' }}>No encontrado: <strong>{fmtNum(f.noEncontrados)}</strong> ({fmtPct(f.pctNoEncontrados)})</p>
      <p style={{ margin: '2px 0', color: 'var(--z-text-muted)' }}>Falta por buscar: <strong>{fmtNum(f.faltaPorBuscar)}</strong> ({fmtPct(f.pctFaltaPorBuscar)})</p>
      <p style={{ margin: '4px 0 0', color: 'var(--z-text-muted)' }}>Último dato: {fmtFecha(f.ultimaConsultaMax)}</p>
    </div>
  );
}

// ╔══════════════════════════════════════════════════════════════════════════════╗
// ║  TAB — RESUMEN                                                               ║
// ╚══════════════════════════════════════════════════════════════════════════════╝
export function TabResumen({ alcance, onAlcance, onRegisterDownload }: {
  alcance: Alcance; onAlcance: (a: Alcance) => void; onRegisterDownload?: DownloadRegistrar;
}) {
  const refGrafico = useRef<HTMLDivElement>(null);
  const latestRef = useRef<ResumenLocalizacion | null>(null);

  const { data, isLoading, isError, error } = useQuery({
    queryKey: ['loc-resumen', alcance.liderID, alcance.carteraID],
    queryFn: () => obtenerResumenLocalizacion({ liderID: alcance.liderID, carteraID: alcance.carteraID }),
    staleTime: 60 * 1000,
  });
  // La descarga lee lo último visible en el momento del clic.
  useEffect(() => { latestRef.current = data ?? null; }, [data]);

  useEffect(() => {
    if (!onRegisterDownload) return;
    onRegisterDownload((): PdfExportDescriptor | null => {
      const d = latestRef.current;
      if (!d || d.fuentes.length === 0) return null;
      const head = ['Fuente', 'Estado', 'Encontrados', '% Enc.', 'No encontrados', '% No enc.', 'Falta por buscar', '% Falta', 'Último dato'];
      const body = d.fuentes.map(f => [
        f.etiqueta, f.estado,
        fmtNum(f.encontrados), fmtPct(f.pctEncontrados),
        fmtNum(f.noEncontrados), fmtPct(f.pctNoEncontrados),
        fmtNum(f.faltaPorBuscar), fmtPct(f.pctFaltaPorBuscar),
        fmtFecha(f.ultimaConsultaMax),
      ]);
      const pdfTable: PdfTableData = {
        head: [head], body, cumpColIndex: -1,
        colStyles: { 0: { halign: 'left' }, 1: { halign: 'center' }, 8: { halign: 'center' } },
      };
      const m = d.metricas;
      return {
        columnas: head,
        generar: idx => buildExportarPDF({
          tituloArchivo: `Localizacion_Resumen_${d.etiquetaAlcance.replace(/\W+/g, '_')}`,
          tituloPDF: `Localización — ${d.etiquetaAlcance}`,
          subtitulo: `Localización al ${fmtFecha(d.corte.fechaCorte)} · ${fmtNum(m.total)} titulares · `
            + `Índice ${m.indice?.toFixed(1) ?? '—'} · Contacto ${fmtPct(m.pctContacto)} · Cobertura ${fmtPct(m.pctCobertura)} · Frescura ${fmtPct(m.pctFrescura)}`,
          pdfTable: filtrarColumnas(pdfTable, idx),
          refGrafico,
        }),
      };
    });
    return () => onRegisterDownload(null);
  }, [onRegisterDownload]);

  const estados = useMemo(
    () => Object.fromEntries((data?.fuentes ?? []).map(f => [f.etiqueta, f.estado])) as Record<string, EstadoFuente>,
    [data],
  );

  const filtros = <FiltrosAlcance alcance={alcance} onChange={onAlcance} />;

  if (isLoading) return <div style={{ display: 'flex', flexDirection: 'column', gap: 16 }}>{filtros}<Cargando /></div>;
  if (isError || !data) {
    const p = problemaLocalizacion(error);
    return (
      <div style={{ display: 'flex', flexDirection: 'column', gap: 16 }}>
        {filtros}
        {p ? <p style={{ textAlign: 'center', padding: '40px 0', color: 'var(--z-text-muted)', fontSize: 13 }}>{p.mensaje}</p> : <ErrorMsg />}
      </div>
    );
  }

  const m = data.metricas;
  const datosGrafico = data.fuentes.map(f => ({
    ...f,
    etiquetaEje: f.etiqueta,
    pEnc: f.pctEncontrados ?? 0,
    pNoEnc: f.pctNoEncontrados ?? 0,
    pFalta: f.pctFaltaPorBuscar ?? 0,
  }));

  return (
    <div style={{ display: 'flex', flexDirection: 'column', gap: 20 }}>
      {filtros}
      <p style={{ margin: 0, fontSize: 12, color: 'var(--z-text-muted)' }}>
        Universo: <strong style={{ color: 'var(--z-text-primary)' }}>{describirAlcance(data.alcance, data.etiquetaAlcance)}</strong>
      </p>

      {/* KPIs */}
      <div style={{ display: 'flex', gap: 12, flexWrap: 'wrap' }}>
        <KpiCard label="Índice de localización" value={m.indice != null ? m.indice.toFixed(1) : '—'} valueSuffix="/ 100"
          color={colorNivel(m.indice)}
          sub={`${Math.round(data.pesos.contacto * 100)} % contacto · ${Math.round(data.pesos.cobertura * 100)} % cobertura · ${Math.round(data.pesos.frescura * 100)} % frescura`} />
        <KpiCard label="Titulares" value={fmtNum(m.total)} />
        <KpiCard label="Con dato de contacto" value={fmtNum(m.conContacto)} color={COLOR_LOC.contacto} sub={`${fmtPct(m.pctContacto)} · celular o email`} />
        <KpiCard label="Sin contacto" value={fmtNum(m.sinContacto)} color={COLOR_LOC.alerta}
          sub={m.total > 0 ? fmtPct((m.sinContacto / m.total) * 100) : undefined} />
      </div>
      <div style={{ display: 'flex', gap: 12, flexWrap: 'wrap' }}>
        <KpiCard label="Sin contacto con saldo" value={m.conSaldo > 0 ? fmtNum(m.sinContactoConSaldo) : "—"} color={COLOR_LOC.alerta}
          sub={m.conSaldo > 0 ? `${cop(m.saldoSinContacto)} por cobrar sin forma de contacto` : "Sin datos de saldo para este alcance"} />
        <KpiCard label="Cobertura funcional" value={fmtPct(m.pctCobertura)} color={COLOR_LOC.cobertura}
          sub={`${fmtNum(m.buscadosFuncional)} buscados en al menos una fuente Funcional`} />
        <KpiCard label="Nunca buscados (funcionales)" value={fmtNum(m.nuncaBuscadosFuncional)} color="#ea580c"
          sub="Sin fila en ninguna fuente Funcional" />
        <KpiCard label="Frescura" value={fmtPct(m.pctFrescura)} color={COLOR_LOC.frescura}
          sub={`${fmtNum(m.frescos180)} con una consulta de hace 180 días o menos`} />
      </div>

      {/* Gráfico por fuente */}
      <Tarjeta
        titulo="Localización por fuente (% de los titulares)"
        derecha={<LeyendaGrafico items={[
          { color: COLOR_LOC.encontrado, label: 'Encontrado' },
          { color: COLOR_LOC.noEncontrado, label: 'No encontrado' },
          { color: COLOR_LOC.falta, label: 'Falta por buscar' },
          { color: COLOR_ESTADO_FUENTE.Funcional, label: '● Funcional' },
          { color: COLOR_ESTADO_FUENTE.Intermitente, label: '● Intermitente' },
          { color: COLOR_ESTADO_FUENTE.Inactivo, label: '● Inactivo' },
        ]} />}
        refContenido={refGrafico}
      >
        <ResponsiveContainer width="100%" height={Math.max(320, datosGrafico.length * 26)}>
          <BarChart data={datosGrafico} layout="vertical" margin={{ left: 20, right: 20, top: 4, bottom: 4 }} barCategoryGap={4}>
            <CartesianGrid horizontal={false} stroke="var(--z-border)" />
            <XAxis type="number" domain={[0, 100]} tickFormatter={v => `${v} %`} tick={{ fontSize: 11, fill: 'var(--z-text-muted)' }} />
            <YAxis type="category" dataKey="etiquetaEje" width={120} tick={<TickFuente estados={estados} />} interval={0} />
            <Tooltip content={<TooltipFuente />} cursor={{ fill: 'var(--z-bg-page)' }} />
            <Bar dataKey="pEnc" stackId="a" name="Encontrado" fill={COLOR_LOC.encontrado} />
            <Bar dataKey="pNoEnc" stackId="a" name="No encontrado" fill={COLOR_LOC.noEncontrado} />
            <Bar dataKey="pFalta" stackId="a" name="Falta por buscar" fill={COLOR_LOC.falta} radius={[0, 4, 4, 0]} />
          </BarChart>
        </ResponsiveContainer>
      </Tarjeta>

      {/* Alertas */}
      <Tarjeta titulo={`Alertas (${data.alertas.length})`}>
        {data.alertas.length === 0 ? (
          <p style={{ margin: 0, fontSize: 13, color: 'var(--z-text-muted)' }}>Sin alertas para este alcance.</p>
        ) : (
          <ul style={{ margin: 0, padding: 0, listStyle: 'none', display: 'flex', flexDirection: 'column', gap: 8 }}>
            {data.alertas.map((a, i) => (
              <li key={i} style={{
                display: 'flex', gap: 10, alignItems: 'flex-start', fontSize: 13, color: 'var(--z-text-primary)',
                padding: '8px 12px', borderRadius: 8,
                background: a.severidad === 'alta' ? 'rgba(220,38,38,0.07)' : 'rgba(202,138,4,0.08)',
                borderLeft: `3px solid ${a.severidad === 'alta' ? '#dc2626' : '#ca8a04'}`,
              }}>
                <span style={{ fontSize: 10, fontWeight: 700, textTransform: 'uppercase', color: a.severidad === 'alta' ? '#dc2626' : '#ca8a04', paddingTop: 2, whiteSpace: 'nowrap' }}>
                  {a.tipo === 'POR_BUSCAR' ? 'Por buscar' : a.tipo === 'DATO_VIEJO' ? 'Dato viejo' : 'Sin contacto con saldo'}
                </span>
                <span>{a.mensaje}</span>
              </li>
            ))}
          </ul>
        )}
      </Tarjeta>

      {/* Alertas de la semana (etapa 4): contra la semana anterior, lotes, fuentes y colas. */}
      <PanelAlertasSemana alcance={alcance} />

      <AvisoNoMedible />
    </div>
  );
}
