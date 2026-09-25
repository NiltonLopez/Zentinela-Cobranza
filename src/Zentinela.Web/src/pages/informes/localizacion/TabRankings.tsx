import { Fragment, useEffect, useRef, useState } from 'react';
import { useQuery } from '@tanstack/react-query';
import { obtenerRankingsLocalizacion, problemaLocalizacion } from '../../../api/informesLocalizacionApi';
import type { CriterioRanking, FilaRankingLider, RankingsLocalizacion } from '../../../types/informesLocalizacion';
import { useAlcanceLider } from '../../../hooks/useAlcanceLider';
import { AvatarPersona } from '../../../components/AvatarPersona';
import {
  Cargando, ErrorMsg, TD, TH, buildExportarPDF, cop, filtrarColumnas,
} from '../../../components/informes/reporteCompartido';
import type { DownloadRegistrar, PdfExportDescriptor, PdfTableData } from '../../../components/informes/reporteCompartido';
import {
  Delta, Opciones, Podio, TD_NUM, Tarjeta, colorNivel, fmtFecha, fmtNum, fmtPct,
} from './compartidos';

const CRITERIOS: { value: CriterioRanking; label: string }[] = [
  { value: 'Indice',           label: 'Índice' },
  { value: 'Contacto',         label: '% Contacto' },
  { value: 'Cobertura',        label: '% Cobertura' },
  { value: 'Frescura',         label: '% Frescura' },
  { value: 'SinContactoSaldo', label: 'Sin contacto con saldo' },
];

const etiquetaCriterio = (c: CriterioRanking) => CRITERIOS.find(x => x.value === c)?.label ?? c;

/** Valor del criterio como se muestra: el índice sin %, el resto como porcentaje. */
const fmtValor = (c: CriterioRanking, v: number | null) =>
  v == null ? '—' : c === 'Indice' ? v.toLocaleString('es-CO', { minimumFractionDigits: 1, maximumFractionDigits: 1 }) : fmtPct(v);

// ╔══════════════════════════════════════════════════════════════════════════════╗
// ║  TAB — RANKINGS CON FOTO                                                     ║
// ╚══════════════════════════════════════════════════════════════════════════════╝
export function TabRankings({ onRegisterDownload }: { onRegisterDownload?: DownloadRegistrar }) {
  const { esLider } = useAlcanceLider();
  const [criterio, setCriterio] = useState<CriterioRanking>('Indice');
  const [incluirExternas, setIncluirExternas] = useState(false);
  const latestRef = useRef<RankingsLocalizacion | null>(null);

  const { data, isLoading, isError, error } = useQuery({
    queryKey: ['loc-rankings', criterio, incluirExternas],
    queryFn: () => obtenerRankingsLocalizacion(criterio, incluirExternas),
    staleTime: 60 * 1000,
  });
  // La descarga lee lo último visible en el momento del clic.
  useEffect(() => { latestRef.current = data ?? null; }, [data]);

  useEffect(() => {
    if (!onRegisterDownload) return;
    onRegisterDownload((): PdfExportDescriptor | null => {
      const d = latestRef.current;
      if (!d || d.lideres.length === 0) return null;
      const c = d.criterio;
      const head = ['#', 'Líder', etiquetaCriterio(c), 'Variación', 'Titulares', 'Índice', '% Contacto', '% Cobertura', '% Frescura', 'Sin contacto c/ saldo', 'Saldo sin contacto'];
      const body = d.lideres.map(l => [
        l.posicion, l.nombre, fmtValor(c, l.valor),
        l.delta == null ? '—' : `${l.delta > 0 ? '+' : ''}${l.delta.toFixed(1)}`,
        fmtNum(l.total), l.indice?.toFixed(1) ?? '—', fmtPct(l.pctContacto), fmtPct(l.pctCobertura), fmtPct(l.pctFrescura),
        fmtNum(l.sinContactoConSaldo), cop(l.saldoSinContacto),
      ]);
      const pdfTable: PdfTableData = {
        head: [head], body, cumpColIndex: -1,
        colStyles: { 0: { halign: 'center' }, 1: { halign: 'left' }, 3: { halign: 'center' } },
      };
      return {
        columnas: head,
        generar: idx => buildExportarPDF({
          tituloArchivo: `Localizacion_Ranking_Lideres_${c}`,
          tituloPDF: `Ranking de líderes — ${etiquetaCriterio(c)}`,
          subtitulo: `Localización al ${fmtFecha(d.corte.fechaCorte)}`
            + (d.corteAnterior ? ` · variación contra el ${fmtFecha(d.corteAnterior.fechaCorte)}` : '')
            + (d.incluirExternas ? ' · incluye relaciones externas y BASE GENERAL' : ' · sin relaciones externas ni BASE GENERAL')
            + (d.recortado ? ` · top y posición propia (${d.miPosicion ?? '—'} de ${d.totalLideres})` : ''),
          pdfTable: filtrarColumnas(pdfTable, idx),
        }),
      };
    });
    return () => onRegisterDownload(null);
  }, [onRegisterDownload]);

  const controles = (
    <div style={{ display: 'flex', gap: 14, flexWrap: 'wrap', alignItems: 'center', justifyContent: 'space-between' }}>
      <Opciones label="Criterio" opciones={CRITERIOS} valor={criterio} onChange={setCriterio} />
      {!esLider && (
        <label style={{ display: 'flex', alignItems: 'center', gap: 6, fontSize: 12, color: 'var(--z-text-muted)', cursor: 'pointer' }}>
          <input type="checkbox" checked={incluirExternas} onChange={e => setIncluirExternas(e.target.checked)} />
          Incluir relaciones externas y BASE GENERAL
        </label>
      )}
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

  const c = data.criterio;
  const detalle = (l: FilaRankingLider) => `${fmtNum(l.total)} titulares`;

  return (
    <div style={{ display: 'flex', flexDirection: 'column', gap: 20 }}>
      {controles}

      {esLider && data.miPosicion != null && (
        <div style={{
          display: 'flex', alignItems: 'center', gap: 10, fontSize: 13, color: 'var(--z-text-primary)',
          background: 'var(--z-brand-light)', border: '1px solid var(--z-brand)', borderRadius: 8, padding: '10px 14px',
        }}>
          Tu posición en <strong>{etiquetaCriterio(c)}</strong>: <strong style={{ fontSize: 16 }}>{data.miPosicion}</strong> de {data.totalLideres} líderes.
        </div>
      )}

      <Tarjeta titulo={`Podio — ${etiquetaCriterio(c)}${data.menorEsMejor ? ' (menos es mejor)' : ''}`}>
        <Podio puestos={data.lideres.map(l => ({
          posicion: l.posicion, id: l.liderID, nombre: l.nombre, documento: l.documento,
          valor: fmtValor(c, l.valor), detalle: detalle(l), esPropio: l.esPropio,
        }))} />
      </Tarjeta>

      <Tarjeta titulo={data.recortado ? `Líderes — los primeros y tu posición (de ${data.totalLideres})` : `Líderes (${data.totalLideres})`}
        derecha={<span style={{ fontSize: 12, color: 'var(--z-text-muted)' }}>
          Variación contra {data.corteAnterior ? `el corte del ${fmtFecha(data.corteAnterior.fechaCorte)}` : '— (aún no hay un corte de un día anterior)'}
        </span>}>
        <div style={{ overflowX: 'auto' }}>
          <table style={{ width: '100%', borderCollapse: 'collapse', fontSize: 13 }}>
            <thead>
              <tr>
                <th style={{ ...TH, textAlign: 'center', width: 48 }}>#</th>
                <th style={{ ...TH, textAlign: 'left' }}>Líder</th>
                <th style={{ ...TH, textAlign: 'right' }}>{etiquetaCriterio(c)}</th>
                <th style={{ ...TH, textAlign: 'center' }}>Variación</th>
                <th style={{ ...TH, textAlign: 'right' }}>Titulares</th>
                <th style={{ ...TH, textAlign: 'right' }}>Índice</th>
                <th style={{ ...TH, textAlign: 'right' }}>% Contacto</th>
                <th style={{ ...TH, textAlign: 'right' }}>% Cobertura</th>
                <th style={{ ...TH, textAlign: 'right' }}>% Frescura</th>
                <th style={{ ...TH, textAlign: 'right' }}>Sin contacto c/ saldo</th>
              </tr>
            </thead>
            <tbody>
              {data.lideres.map((l, i) => {
                // En la vista recortada del Líder, un separador marca el salto entre el top y su fila.
                const salto = i > 0 && l.posicion - data.lideres[i - 1].posicion > 1 && data.recortado;
                return (
                  <Fragment key={l.liderID}>
                    {salto && (
                      <tr><td colSpan={10} style={{ ...TD, textAlign: 'center', color: 'var(--z-text-muted)', padding: '4px 0' }}>⋯</td></tr>
                    )}
                    <tr style={{ background: l.esPropio ? 'var(--z-brand-light)' : 'transparent' }}>
                      <td style={{ ...TD, textAlign: 'center', fontWeight: 700, color: 'var(--z-text-primary)' }}>{l.posicion}</td>
                      <td style={{ ...TD, fontWeight: 500, color: 'var(--z-text-primary)' }}>
                        <span style={{ display: 'inline-flex', alignItems: 'center', gap: 10 }}>
                          <AvatarPersona documento={l.documento} nombre={l.nombre} tam={32} />
                          {l.nombre}{l.esPropio ? ' (tú)' : ''}
                        </span>
                      </td>
                      <td style={{ ...TD, ...TD_NUM, fontWeight: 700, color: c === 'Indice' ? colorNivel(l.valor) : 'var(--z-text-primary)' }}>{fmtValor(c, l.valor)}</td>
                      <td style={{ ...TD, textAlign: 'center' }}><Delta valor={l.delta} menorEsMejor={data.menorEsMejor} /></td>
                      <td style={{ ...TD, ...TD_NUM }}>{fmtNum(l.total)}</td>
                      <td style={{ ...TD, ...TD_NUM, color: colorNivel(l.indice) }}>{l.indice?.toFixed(1) ?? '—'}</td>
                      <td style={{ ...TD, ...TD_NUM }}>{fmtPct(l.pctContacto)}</td>
                      <td style={{ ...TD, ...TD_NUM }}>{fmtPct(l.pctCobertura)}</td>
                      <td style={{ ...TD, ...TD_NUM }}>{fmtPct(l.pctFrescura)}</td>
                      <td style={{ ...TD, ...TD_NUM }} title={cop(l.saldoSinContacto)}>
                        {fmtNum(l.sinContactoConSaldo)} <span style={{ color: 'var(--z-text-muted)', fontSize: 11 }}>({cop(l.saldoSinContacto)})</span>
                      </td>
                    </tr>
                  </Fragment>
                );
              })}
            </tbody>
          </table>
        </div>
        <p style={{ margin: '10px 0 0', fontSize: 11, color: 'var(--z-text-muted)' }}>
          El universo de cada líder son sus titulares distintos (un titular en dos de sus carteras cuenta una vez).
          {!data.incluirExternas && ' Sin relaciones externas ni BASE GENERAL.'} Empates a un decimal comparten posición.
        </p>
      </Tarjeta>

      <Tarjeta titulo={data.carterasSonPropias ? 'Tus carteras' : `Carteras (${data.carteras.length})`}>
        <div style={{ overflowX: 'auto' }}>
          <table style={{ width: '100%', borderCollapse: 'collapse', fontSize: 13 }}>
            <thead>
              <tr>
                <th style={{ ...TH, textAlign: 'center', width: 48 }}>#</th>
                <th style={{ ...TH, textAlign: 'left' }}>Cartera</th>
                {!data.carterasSonPropias && <th style={{ ...TH, textAlign: 'center' }}>Líderes</th>}
                <th style={{ ...TH, textAlign: 'right' }}>{etiquetaCriterio(c)}</th>
                <th style={{ ...TH, textAlign: 'center' }}>Variación</th>
                <th style={{ ...TH, textAlign: 'right' }}>Titulares</th>
                <th style={{ ...TH, textAlign: 'right' }}>Índice</th>
                <th style={{ ...TH, textAlign: 'right' }}>% Contacto</th>
                <th style={{ ...TH, textAlign: 'right' }}>% Cobertura</th>
                <th style={{ ...TH, textAlign: 'right' }}>% Frescura</th>
                <th style={{ ...TH, textAlign: 'right' }}>Sin contacto c/ saldo</th>
              </tr>
            </thead>
            <tbody>
              {data.carteras.map(k => (
                <tr key={`${k.carteraID}-${k.liderID ?? 0}`}
                  onMouseEnter={e => { e.currentTarget.style.background = 'var(--z-bg-page)'; }}
                  onMouseLeave={e => { e.currentTarget.style.background = 'transparent'; }}>
                  <td style={{ ...TD, textAlign: 'center', fontWeight: 700 }}>{k.posicion}</td>
                  <td style={{ ...TD, fontWeight: 500, color: 'var(--z-text-primary)' }}>{k.nombre}</td>
                  {!data.carterasSonPropias && <td style={{ ...TD, textAlign: 'center', color: 'var(--z-text-muted)' }}>{k.lideres}</td>}
                  <td style={{ ...TD, ...TD_NUM, fontWeight: 700, color: c === 'Indice' ? colorNivel(k.valor) : 'var(--z-text-primary)' }}>{fmtValor(c, k.valor)}</td>
                  <td style={{ ...TD, textAlign: 'center' }}><Delta valor={k.delta} menorEsMejor={data.menorEsMejor} /></td>
                  <td style={{ ...TD, ...TD_NUM }}>{fmtNum(k.total)}</td>
                  <td style={{ ...TD, ...TD_NUM, color: colorNivel(k.indice) }}>{k.indice?.toFixed(1) ?? '—'}</td>
                  <td style={{ ...TD, ...TD_NUM }}>{fmtPct(k.pctContacto)}</td>
                  <td style={{ ...TD, ...TD_NUM }}>{fmtPct(k.pctCobertura)}</td>
                  <td style={{ ...TD, ...TD_NUM }}>{fmtPct(k.pctFrescura)}</td>
                  <td style={{ ...TD, ...TD_NUM }}>
                    {fmtNum(k.sinContactoConSaldo)} <span style={{ color: 'var(--z-text-muted)', fontSize: 11 }}>({cop(k.saldoSinContacto)})</span>
                  </td>
                </tr>
              ))}
            </tbody>
          </table>
        </div>
        {data.carterasSonPropias && (
          <p style={{ margin: '10px 0 0', fontSize: 11, color: 'var(--z-text-muted)' }}>
            Cada fila es tu parte de la cartera (tus titulares en ella), ordenadas entre sí.
          </p>
        )}
      </Tarjeta>
    </div>
  );
}
