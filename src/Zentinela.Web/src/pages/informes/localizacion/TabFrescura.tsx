import { useEffect, useRef, useState } from 'react';
import { useQuery } from '@tanstack/react-query';
import { ResponsiveContainer, BarChart, Bar, XAxis, YAxis, CartesianGrid, Tooltip } from 'recharts';
import { obtenerFrescuraLocalizacion, problemaLocalizacion } from '../../../api/informesLocalizacionApi';
import type { FilaFrescura, FrescuraLocalizacion, UmbralFrescura } from '../../../types/informesLocalizacion';
import {
  Cargando, ErrorMsg, LeyendaGrafico, TD, TH, buildExportarPDF, filtrarColumnas,
} from '../../../components/informes/reporteCompartido';
import type { DownloadRegistrar, PdfExportDescriptor, PdfTableData } from '../../../components/informes/reporteCompartido';
import {
  COLOR_TRAMO, ChipEstadoFuente, FiltrosAlcance, Opciones, TD_NUM, Tarjeta, describirAlcance, diasDesde,
  fmtFecha, fmtNum, fmtPct, type Alcance,
} from './compartidos';

const TRAMOS = [
  { k: 'hasta90',   label: 'Hasta 90 días',  color: COLOR_TRAMO.hasta90 },
  { k: 'de91a180',  label: '91 a 180 días',  color: COLOR_TRAMO.de91a180 },
  { k: 'de181a365', label: '181 a 365 días', color: COLOR_TRAMO.de181a365 },
  { k: 'mas365',    label: 'Más de 365 días', color: COLOR_TRAMO.mas365 },
] as const;

function TooltipTramos({ active, payload }: { active?: boolean; payload?: { payload: FilaFrescura }[] }) {
  if (!active || !payload?.length) return null;
  const f = payload[0].payload;
  return (
    <div style={{ background: 'var(--z-bg-card)', border: '1px solid var(--z-border)', borderRadius: 8, padding: '10px 14px', fontSize: 12 }}>
      <p style={{ fontWeight: 600, margin: '0 0 4px', color: 'var(--z-text-primary)' }}>{f.etiqueta} · {fmtNum(f.buscados)} buscados</p>
      {TRAMOS.map(t => (
        <p key={t.k} style={{ margin: '2px 0', color: t.color }}>
          {t.label}: <strong>{fmtNum(f[t.k])}</strong>
        </p>
      ))}
    </div>
  );
}

// ╔══════════════════════════════════════════════════════════════════════════════╗
// ║  TAB — FRESCURA                                                              ║
// ╚══════════════════════════════════════════════════════════════════════════════╝
export function TabFrescura({ alcance, onAlcance, onRegisterDownload }: {
  alcance: Alcance; onAlcance: (a: Alcance) => void; onRegisterDownload?: DownloadRegistrar;
}) {
  const [umbral, setUmbral] = useState<UmbralFrescura>(180);
  const refGrafico = useRef<HTMLDivElement>(null);
  const latestRef = useRef<FrescuraLocalizacion | null>(null);

  const { data, isLoading, isError, error } = useQuery({
    queryKey: ['loc-frescura', alcance.liderID, alcance.carteraID, umbral],
    queryFn: () => obtenerFrescuraLocalizacion({ liderID: alcance.liderID, carteraID: alcance.carteraID }, umbral),
    staleTime: 60 * 1000,
  });
  // La descarga lee lo último visible en el momento del clic.
  useEffect(() => { latestRef.current = data ?? null; }, [data]);

  useEffect(() => {
    if (!onRegisterDownload) return;
    onRegisterDownload((): PdfExportDescriptor | null => {
      const d = latestRef.current;
      if (!d || d.fuentes.length === 0) return null;
      const head = ['Fuente', 'Estado', 'Buscados', '≤ 90 d', '91–180 d', '181–365 d', '> 365 d', `Viejos (> ${d.umbral} d)`, '% viejos', 'Último dato', 'Último cargue OK'];
      const body = d.fuentes.map(f => [
        f.etiqueta, f.estado, fmtNum(f.buscados), fmtNum(f.hasta90), fmtNum(f.de91a180), fmtNum(f.de181a365),
        fmtNum(f.mas365), fmtNum(f.viejos), fmtPct(f.pctViejos), fmtFecha(f.ultimaConsultaMax), fmtFecha(f.ultimoCargueOk),
      ]);
      const pdfTable: PdfTableData = {
        head: [head], body, cumpColIndex: -1,
        colStyles: { 0: { halign: 'left' }, 1: { halign: 'center' }, 9: { halign: 'center' }, 10: { halign: 'center' } },
      };
      return {
        columnas: head,
        generar: idx => buildExportarPDF({
          tituloArchivo: `Localizacion_Frescura_${d.etiquetaAlcance.replace(/\W+/g, '_')}`,
          tituloPDF: `Frescura de la localización — ${d.etiquetaAlcance}`,
          subtitulo: `Localización al ${fmtFecha(d.corte.fechaCorte)} · antigüedad de la última consulta por fuente · dato viejo: más de ${d.umbral} días`,
          pdfTable: filtrarColumnas(pdfTable, idx),
          refGrafico,
        }),
      };
    });
    return () => onRegisterDownload(null);
  }, [onRegisterDownload]);

  const filtros = (
    <div style={{ display: 'flex', gap: 14, flexWrap: 'wrap', alignItems: 'center', justifyContent: 'space-between' }}>
      <FiltrosAlcance alcance={alcance} onChange={onAlcance} />
      <Opciones<UmbralFrescura>
        label="Dato viejo: más de"
        opciones={[{ value: 90, label: '90 días' }, { value: 180, label: '180 días' }, { value: 365, label: '365 días' }]}
        valor={umbral}
        onChange={setUmbral}
      />
    </div>
  );

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

  const conBuscados = data.fuentes.filter(f => f.buscados > 0);

  return (
    <div style={{ display: 'flex', flexDirection: 'column', gap: 20 }}>
      {filtros}
      <p style={{ margin: 0, fontSize: 12, color: 'var(--z-text-muted)' }}>
        Universo: <strong style={{ color: 'var(--z-text-primary)' }}>{describirAlcance(data.alcance, data.etiquetaAlcance)}</strong>.
        Cada titular buscado cuenta en el tramo de su <em>última</em> consulta en la fuente.
      </p>

      <div style={{
        fontSize: 12, color: '#92400e', background: 'rgba(245,158,11,0.10)', border: '1px solid rgba(245,158,11,0.35)',
        borderRadius: 8, padding: '10px 14px',
      }}>
        Los procedimientos legacy de cargue no refrescaban la fecha cuando el contenido no cambiaba (ADR-039): en las
        fuentes que se cargaban por ahí, un dato "viejo" puede haberse confirmado después sin que quedara registrado.
        Desde el Cargue de Fuentes la fecha sí se refresca.
      </div>

      <Tarjeta
        titulo="Antigüedad de la última consulta por fuente (titulares buscados)"
        derecha={<LeyendaGrafico items={TRAMOS.map(t => ({ color: t.color, label: t.label }))} />}
        refContenido={refGrafico}
      >
        <ResponsiveContainer width="100%" height={Math.max(300, conBuscados.length * 26)}>
          <BarChart data={conBuscados} layout="vertical" margin={{ left: 20, right: 20, top: 4, bottom: 4 }} barCategoryGap={4} stackOffset="expand">
            <CartesianGrid horizontal={false} stroke="var(--z-border)" />
            <XAxis type="number" tickFormatter={v => `${Math.round(v * 100)} %`} tick={{ fontSize: 11, fill: 'var(--z-text-muted)' }} />
            <YAxis type="category" dataKey="etiqueta" width={110} tick={{ fontSize: 11, fill: 'var(--z-text-primary)' }} interval={0} />
            <Tooltip content={<TooltipTramos />} cursor={{ fill: 'var(--z-bg-page)' }} />
            {TRAMOS.map((t, i) => (
              <Bar key={t.k} dataKey={t.k} stackId="t" name={t.label} fill={t.color}
                radius={i === TRAMOS.length - 1 ? [0, 4, 4, 0] : undefined} />
            ))}
          </BarChart>
        </ResponsiveContainer>
      </Tarjeta>

      <div style={{ background: 'var(--z-bg-card)', border: '1px solid var(--z-border)', borderRadius: 10, overflow: 'hidden' }}>
        <div style={{ overflowX: 'auto' }}>
          <table style={{ width: '100%', borderCollapse: 'collapse', fontSize: 13 }}>
            <thead>
              <tr>
                <th style={{ ...TH, textAlign: 'left' }}>Fuente</th>
                <th style={{ ...TH, textAlign: 'center' }}>Estado</th>
                <th style={{ ...TH, textAlign: 'right' }}>Buscados</th>
                {TRAMOS.map(t => <th key={t.k} style={{ ...TH, textAlign: 'right', color: t.color }}>{t.label}</th>)}
                <th style={{ ...TH, textAlign: 'right' }}>Viejos (&gt; {data.umbral} d)</th>
                <th style={{ ...TH, textAlign: 'center' }}>Último dato</th>
                <th style={{ ...TH, textAlign: 'center' }}>Último cargue OK</th>
              </tr>
            </thead>
            <tbody>
              {data.fuentes.map(f => {
                const dias = diasDesde(f.ultimaConsultaMax);
                return (
                  <tr key={f.clave}
                    onMouseEnter={e => { e.currentTarget.style.background = 'var(--z-bg-page)'; }}
                    onMouseLeave={e => { e.currentTarget.style.background = 'transparent'; }}>
                    <td style={{ ...TD, fontWeight: 500, color: 'var(--z-text-primary)' }}>{f.etiqueta}</td>
                    <td style={{ ...TD, textAlign: 'center' }}><ChipEstadoFuente estado={f.estado} /></td>
                    <td style={{ ...TD, ...TD_NUM }}>{fmtNum(f.buscados)}</td>
                    {TRAMOS.map(t => <td key={t.k} style={{ ...TD, ...TD_NUM }}>{fmtNum(f[t.k])}</td>)}
                    <td style={{ ...TD, ...TD_NUM, fontWeight: 600, color: (f.pctViejos ?? 0) > 50 ? '#dc2626' : 'var(--z-text-primary)' }}>
                      {fmtNum(f.viejos)} <span style={{ fontWeight: 400, color: 'var(--z-text-muted)' }}>({fmtPct(f.pctViejos)})</span>
                    </td>
                    <td style={{ ...TD, textAlign: 'center', color: dias != null && dias > 365 ? '#dc2626' : 'var(--z-text-muted)' }}>
                      {fmtFecha(f.ultimaConsultaMax)}
                    </td>
                    <td style={{ ...TD, textAlign: 'center', color: 'var(--z-text-muted)' }}>{f.ultimoCargueOk ? fmtFecha(f.ultimoCargueOk) : '—'}</td>
                  </tr>
                );
              })}
            </tbody>
          </table>
        </div>
      </div>
    </div>
  );
}
