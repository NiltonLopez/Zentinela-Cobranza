import { useEffect, useMemo, useRef, useState } from 'react';
import { useQuery } from '@tanstack/react-query';
import { ResponsiveContainer, LineChart, Line, XAxis, YAxis, CartesianGrid, Tooltip } from 'recharts';
import { obtenerTendenciaLocalizacion, problemaLocalizacion } from '../../../api/informesLocalizacionApi';
import type { TendenciaLocalizacion } from '../../../types/informesLocalizacion';
import { ZDropdown } from '../../../components/ZDropdown';
import {
  Cargando, ErrorMsg, LeyendaGrafico, TD, TH, buildExportarPDF, filtrarColumnas,
} from '../../../components/informes/reporteCompartido';
import type { DownloadRegistrar, PdfExportDescriptor, PdfTableData } from '../../../components/informes/reporteCompartido';
import {
  FiltrosAlcance, Opciones, TD_NUM, Tarjeta, colorNivel, describirAlcance, fmtFecha, fmtNum, fmtPct, type Alcance,
} from './compartidos';

// Series del gráfico principal. Colores validados como paleta categórica (daltonismo y contraste, claro y
// oscuro); cada línea además lleva su propio trazo (secondary encoding) y la tabla de abajo es la vista accesible.
type Serie = 'indice' | 'pctContacto' | 'pctCobertura' | 'pctFrescura';
const SERIES: { key: Serie; label: string; color: string; dash?: string }[] = [
  { key: 'indice',       label: 'Índice',      color: '#ea580c' },
  { key: 'pctContacto',  label: '% Contacto',  color: '#2563eb', dash: '6 3' },
  { key: 'pctCobertura', label: '% Cobertura', color: '#9333ea', dash: '2 3' },
  { key: 'pctFrescura',  label: '% Frescura',  color: '#0d9488', dash: '8 3 2 3' },
];
const COLOR_SIN_CONTACTO = '#dc2626';
const COLOR_LOCALIZACION = '#16a34a';
const COLOR_COBERTURA_FUENTE = '#9333ea';

const SEMANAS = [
  { value: 8, label: '8 semanas' },
  { value: 12, label: '12 semanas' },
  { value: 26, label: '26 semanas' },
];

/** "21 sep": el lunes de la semana. */
const etiquetaSemana = (iso: string) =>
  new Date(iso).toLocaleDateString('es-CO', { day: '2-digit', month: 'short' });

/** Eje Y: de un múltiplo de 10 bajo el mínimo hasta 100 (los porcentajes rara vez bajan de 30: un 0–100 los aplana). */
function dominio(valores: (number | null | undefined)[]): [number, number] {
  const v = valores.filter((x): x is number => x != null);
  if (v.length === 0) return [0, 100];
  return [Math.max(0, Math.floor((Math.min(...v) - 5) / 10) * 10), 100];
}

interface PuntoTooltip { name: string; value: number | null; color: string; dataKey: string }

function TooltipSemana({ active, payload, label }: { active?: boolean; payload?: PuntoTooltip[]; label?: string }) {
  if (!active || !payload?.length) return null;
  return (
    <div style={{ background: 'var(--z-bg-card)', border: '1px solid var(--z-border)', borderRadius: 8, padding: '10px 14px', fontSize: 12 }}>
      <p style={{ fontWeight: 600, margin: '0 0 4px', color: 'var(--z-text-primary)' }}>Semana del {label}</p>
      {payload.map(p => (
        <p key={p.dataKey} style={{ margin: '2px 0', color: 'var(--z-text-primary)', display: 'flex', alignItems: 'center', gap: 6 }}>
          <span style={{ width: 10, height: 3, background: p.color, display: 'inline-block', borderRadius: 2 }} />
          {p.name}: <strong>{p.dataKey === 'indice' ? (p.value?.toLocaleString('es-CO', { maximumFractionDigits: 1 }) ?? '—') : fmtPct(p.value)}</strong>
        </p>
      ))}
    </div>
  );
}

// ╔══════════════════════════════════════════════════════════════════════════════╗
// ║  TAB — TENDENCIA SEMANAL (ADR-040, etapa 4)                                  ║
// ║  El último corte OK de cada semana (lunes a domingo), mismo universo que el  ║
// ║  Resumen. Las semanas sin cortes no aparecen.                                ║
// ╚══════════════════════════════════════════════════════════════════════════════╝
export function TabTendencia({ alcance, onAlcance, onRegisterDownload }: {
  alcance: Alcance; onAlcance: (a: Alcance) => void; onRegisterDownload?: DownloadRegistrar;
}) {
  const [semanas, setSemanas] = useState(12);
  const [visibles, setVisibles] = useState<Serie[]>(SERIES.map(s => s.key));
  const [fuente, setFuente] = useState('Fosyga');
  const refGrafico = useRef<HTMLDivElement>(null);
  const latestRef = useRef<TendenciaLocalizacion | null>(null);

  const { data, isLoading, isError, error } = useQuery({
    queryKey: ['loc-tendencia', alcance.liderID, alcance.carteraID, semanas],
    queryFn: () => obtenerTendenciaLocalizacion({ liderID: alcance.liderID, carteraID: alcance.carteraID }, semanas),
    staleTime: 5 * 60 * 1000,
  });
  useEffect(() => { latestRef.current = data ?? null; }, [data]);

  useEffect(() => {
    if (!onRegisterDownload) return;
    onRegisterDownload((): PdfExportDescriptor | null => {
      const d = latestRef.current;
      if (!d || d.semanas.length === 0) return null;
      const head = ['Semana', 'Corte', 'Titulares', 'Índice', '% Contacto', '% Cobertura', '% Frescura', '% Sin contacto c/ saldo'];
      const body = d.semanas.map(w => [
        etiquetaSemana(w.inicioSemana), fmtFecha(w.fechaCorte), fmtNum(w.metricas.total),
        w.metricas.indice?.toFixed(1) ?? '—', fmtPct(w.metricas.pctContacto), fmtPct(w.metricas.pctCobertura),
        fmtPct(w.metricas.pctFrescura), fmtPct(w.metricas.pctSinContactoConSaldo),
      ]);
      const pdfTable: PdfTableData = { head: [head], body, cumpColIndex: -1, colStyles: { 0: { halign: 'left' }, 1: { halign: 'center' } } };
      return {
        columnas: head,
        generar: idx => buildExportarPDF({
          tituloArchivo: `Localizacion_Tendencia_${d.etiquetaAlcance.replace(/\W+/g, '_')}`,
          tituloPDF: `Tendencia de localización — ${d.etiquetaAlcance}`,
          subtitulo: `Últimas ${d.semanasPedidas} semanas · un corte por semana (el último de cada una)`,
          pdfTable: filtrarColumnas(pdfTable, idx),
          refGrafico,
        }),
      };
    });
    return () => onRegisterDownload(null);
  }, [onRegisterDownload]);

  const puntos = useMemo(() => (data?.semanas ?? []).map(w => ({
    semana: etiquetaSemana(w.inicioSemana),
    indice: w.metricas.indice,
    pctContacto: w.metricas.pctContacto,
    pctCobertura: w.metricas.pctCobertura,
    pctFrescura: w.metricas.pctFrescura,
    pctSinContacto: w.metricas.pctSinContactoConSaldo,
  })), [data]);

  const fuenteSel = data?.fuentes.find(f => f.clave === fuente) ?? data?.fuentes[0];
  const puntosFuente = (fuenteSel?.puntos ?? []).map(p => ({
    semana: etiquetaSemana(p.inicioSemana), pctLocalizacion: p.pctLocalizacion, pctCobertura: p.pctCobertura,
  }));

  const controles = (
    <div style={{ display: 'flex', gap: 14, flexWrap: 'wrap', alignItems: 'center', justifyContent: 'space-between' }}>
      <FiltrosAlcance alcance={alcance} onChange={onAlcance} />
      <Opciones label="Período" opciones={SEMANAS} valor={semanas} onChange={setSemanas} />
    </div>
  );

  if (isLoading) return <div style={{ display: 'flex', flexDirection: 'column', gap: 16 }}>{controles}<Cargando /></div>;
  if (isError || !data) {
    const p = problemaLocalizacion(error);
    return (
      <div style={{ display: 'flex', flexDirection: 'column', gap: 16 }}>
        {controles}
        {p ? <p style={{ textAlign: 'center', padding: '40px 0', color: 'var(--z-text-muted)', fontSize: 13 }}>{p.mensaje}</p> : <ErrorMsg />}
      </div>
    );
  }

  const toggle = (k: Serie) =>
    setVisibles(v => (v.includes(k) ? (v.length > 1 ? v.filter(x => x !== k) : v) : [...v, k]));
  const dom = dominio(puntos.flatMap(p => visibles.map(k => p[k])));
  const eje = { tick: { fontSize: 11, fill: 'var(--z-text-muted)' }, stroke: 'var(--z-border)' };

  return (
    <div style={{ display: 'flex', flexDirection: 'column', gap: 20 }}>
      {controles}
      <p style={{ margin: 0, fontSize: 12, color: 'var(--z-text-muted)' }}>
        Universo: <strong style={{ color: 'var(--z-text-primary)' }}>{describirAlcance(data.alcance, data.etiquetaAlcance)}</strong>
        {' · '}un punto por semana: el último corte OK de esa semana (lunes a domingo).
      </p>

      {data.semanas.length < 2 && (
        <div style={{ fontSize: 12, color: '#92400e', background: 'rgba(245,158,11,0.10)', border: '1px solid rgba(245,158,11,0.35)', borderRadius: 8, padding: '10px 14px', lineHeight: 1.5 }}>
          {data.semanas.length === 0
            ? 'Todavía no hay cortes en este período.'
            : 'Por ahora hay una sola semana con cortes: la tendencia aparece desde la segunda (un corte cada día a las 13:05 y tras cada cargue).'}
        </div>
      )}

      <Tarjeta
        titulo="Índice y porcentajes por semana"
        derecha={
          <div style={{ display: 'flex', gap: 6, flexWrap: 'wrap' }}>
            {SERIES.map(s => {
              const on = visibles.includes(s.key);
              return (
                <button key={s.key} onClick={() => toggle(s.key)} aria-pressed={on} style={{
                  display: 'flex', alignItems: 'center', gap: 6, padding: '3px 10px', borderRadius: 6, fontSize: 12,
                  border: '1px solid var(--z-border)', background: on ? 'var(--z-bg-page)' : 'transparent',
                  color: on ? 'var(--z-text-primary)' : 'var(--z-text-muted)', cursor: 'pointer', opacity: on ? 1 : 0.6,
                }}>
                  <span style={{ width: 14, height: 0, borderTop: `2px ${s.dash ? 'dashed' : 'solid'} ${s.color}`, display: 'inline-block' }} />
                  {s.label}
                </button>
              );
            })}
          </div>
        }
        refContenido={refGrafico}
      >
        <ResponsiveContainer width="100%" height={300}>
          <LineChart data={puntos} margin={{ left: 0, right: 24, top: 8, bottom: 4 }}>
            <CartesianGrid vertical={false} stroke="var(--z-border)" />
            <XAxis dataKey="semana" {...eje} />
            <YAxis domain={dom} tickFormatter={v => `${v}`} width={36} {...eje} />
            <Tooltip content={<TooltipSemana />} cursor={{ stroke: 'var(--z-border)' }} />
            {SERIES.filter(s => visibles.includes(s.key)).map(s => (
              <Line key={s.key} type="monotone" dataKey={s.key} name={s.label} stroke={s.color} strokeWidth={2}
                strokeDasharray={s.dash} dot={{ r: 4, strokeWidth: 2, fill: 'var(--z-bg-card)' }} activeDot={{ r: 5 }}
                connectNulls isAnimationActive={false} />
            ))}
          </LineChart>
        </ResponsiveContainer>
        <p style={{ margin: '6px 0 0', fontSize: 11, color: 'var(--z-text-muted)' }}>
          Índice 0–100 = {Math.round(data.pesos.contacto * 100)} % contacto + {Math.round(data.pesos.cobertura * 100)} % cobertura
          funcional + {Math.round(data.pesos.frescura * 100)} % frescura. Los porcentajes, sobre los titulares del universo.
        </p>
      </Tarjeta>

      <div style={{ display: 'flex', gap: 20, flexWrap: 'wrap' }}>
        <div style={{ flex: '1 1 420px', minWidth: 0 }}>
          <Tarjeta titulo="% sin contacto con saldo (menos es mejor)">
            <ResponsiveContainer width="100%" height={220}>
              <LineChart data={puntos} margin={{ left: 0, right: 20, top: 8, bottom: 4 }}>
                <CartesianGrid vertical={false} stroke="var(--z-border)" />
                <XAxis dataKey="semana" {...eje} />
                <YAxis domain={[0, 'auto']} width={36} {...eje} />
                <Tooltip content={<TooltipSemana />} cursor={{ stroke: 'var(--z-border)' }} />
                <Line type="monotone" dataKey="pctSinContacto" name="Sin contacto con saldo" stroke={COLOR_SIN_CONTACTO} strokeWidth={2}
                  dot={{ r: 4, strokeWidth: 2, fill: 'var(--z-bg-card)' }} activeDot={{ r: 5 }} connectNulls isAnimationActive={false} />
              </LineChart>
            </ResponsiveContainer>
            {puntos.every(p => p.pctSinContacto == null) && (
              <p style={{ margin: 0, fontSize: 12, color: 'var(--z-text-muted)' }}>Sin datos de saldo para este alcance.</p>
            )}
          </Tarjeta>
        </div>
        <div style={{ flex: '1 1 420px', minWidth: 0 }}>
          <Tarjeta
            titulo="Por fuente"
            derecha={
              <ZDropdown
                buscable
                ariaLabel="Fuente"
                options={data.fuentes.map(f => ({ value: f.clave, label: `${f.etiqueta} (${f.estado})` }))}
                value={fuenteSel?.clave ?? ''}
                onChange={setFuente}
                style={{ minWidth: 200 }}
              />
            }
          >
            <ResponsiveContainer width="100%" height={196}>
              <LineChart data={puntosFuente} margin={{ left: 0, right: 20, top: 8, bottom: 4 }}>
                <CartesianGrid vertical={false} stroke="var(--z-border)" />
                <XAxis dataKey="semana" {...eje} />
                <YAxis domain={dominio(puntosFuente.flatMap(p => [p.pctLocalizacion, p.pctCobertura]))} width={36} {...eje} />
                <Tooltip content={<TooltipSemana />} cursor={{ stroke: 'var(--z-border)' }} />
                <Line type="monotone" dataKey="pctLocalizacion" name="% Localización" stroke={COLOR_LOCALIZACION} strokeWidth={2}
                  dot={{ r: 4, strokeWidth: 2, fill: 'var(--z-bg-card)' }} activeDot={{ r: 5 }} connectNulls isAnimationActive={false} />
                <Line type="monotone" dataKey="pctCobertura" name="% Cobertura" stroke={COLOR_COBERTURA_FUENTE} strokeWidth={2} strokeDasharray="6 3"
                  dot={{ r: 4, strokeWidth: 2, fill: 'var(--z-bg-card)' }} activeDot={{ r: 5 }} connectNulls isAnimationActive={false} />
              </LineChart>
            </ResponsiveContainer>
            <LeyendaGrafico items={[
              { color: COLOR_LOCALIZACION, label: '% Localización (encontrados / titulares)' },
              { color: COLOR_COBERTURA_FUENTE, label: '% Cobertura (buscados / titulares)' },
            ]} />
          </Tarjeta>
        </div>
      </div>

      <Tarjeta titulo={`Semanas (${data.semanas.length})`}>
        <div style={{ overflowX: 'auto' }}>
          <table style={{ width: '100%', borderCollapse: 'collapse', fontSize: 13 }}>
            <thead>
              <tr>
                <th style={{ ...TH, textAlign: 'left' }}>Semana</th>
                <th style={{ ...TH, textAlign: 'left' }}>Corte</th>
                <th style={{ ...TH, textAlign: 'right' }}>Titulares</th>
                <th style={{ ...TH, textAlign: 'right' }}>Índice</th>
                <th style={{ ...TH, textAlign: 'right' }}>% Contacto</th>
                <th style={{ ...TH, textAlign: 'right' }}>% Cobertura</th>
                <th style={{ ...TH, textAlign: 'right' }}>% Frescura</th>
                <th style={{ ...TH, textAlign: 'right' }}>Sin contacto c/ saldo</th>
              </tr>
            </thead>
            <tbody>
              {[...data.semanas].reverse().map(w => (
                <tr key={w.corteID}>
                  <td style={{ ...TD, fontWeight: 500, color: 'var(--z-text-primary)' }}>{etiquetaSemana(w.inicioSemana)}</td>
                  <td style={{ ...TD, color: 'var(--z-text-muted)' }}>{fmtFecha(w.fechaCorte)}</td>
                  <td style={{ ...TD, ...TD_NUM }}>{fmtNum(w.metricas.total)}</td>
                  <td style={{ ...TD, ...TD_NUM, fontWeight: 600, color: colorNivel(w.metricas.indice) }}>{w.metricas.indice?.toFixed(1) ?? '—'}</td>
                  <td style={{ ...TD, ...TD_NUM }}>{fmtPct(w.metricas.pctContacto)}</td>
                  <td style={{ ...TD, ...TD_NUM }}>{fmtPct(w.metricas.pctCobertura)}</td>
                  <td style={{ ...TD, ...TD_NUM }}>{fmtPct(w.metricas.pctFrescura)}</td>
                  <td style={{ ...TD, ...TD_NUM }}>
                    {w.metricas.conSaldo > 0 ? `${fmtNum(w.metricas.sinContactoConSaldo)} (${fmtPct(w.metricas.pctSinContactoConSaldo)})` : '—'}
                  </td>
                </tr>
              ))}
            </tbody>
          </table>
        </div>
      </Tarjeta>
    </div>
  );
}
