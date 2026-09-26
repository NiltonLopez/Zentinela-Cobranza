import { useEffect, useRef, useState } from 'react';
import { useQuery } from '@tanstack/react-query';
import { obtenerAsesoresLocalizacion, problemaLocalizacion } from '../../../api/informesLocalizacionApi';
import type { AsesoresLocalizacion, CriterioAsesor, FilaAsesor } from '../../../types/informesLocalizacion';
import { AvatarPersona } from '../../../components/AvatarPersona';
import {
  Cargando, ErrorMsg, TD, TH, buildExportarPDF, cop, filtrarColumnas,
} from '../../../components/informes/reporteCompartido';
import type { DownloadRegistrar, PdfExportDescriptor, PdfTableData } from '../../../components/informes/reporteCompartido';
import {
  AvisoNoMedible, Delta, FiltrosAlcance, Opciones, Podio, TD_NUM, Tarjeta, colorNivel, describirAlcance, fmtFecha, fmtNum, fmtPct, type Alcance,
} from './compartidos';

const CRITERIOS: { value: CriterioAsesor; label: string }[] = [
  { value: 'Contactabilidad',  label: 'Contactabilidad' },
  { value: 'Gestion',          label: 'Gestión reciente' },
  { value: 'ConDato',          label: '% Con dato de contacto' },
  { value: 'SinContactoSaldo', label: 'Sin contacto con saldo' },
];

const etiquetaCriterio = (c: CriterioAsesor) => CRITERIOS.find(x => x.value === c)?.label ?? c;

// ╔══════════════════════════════════════════════════════════════════════════════╗
// ║  TAB — ASESORES CON FOTO (ADR-040, etapa 4)                                  ║
// ║  Por asesor activo: los titulares del alcance cuya ÚLTIMA gestión hizo él     ║
// ║  (UltimoEstadoTitular.AgenteID = cédula). Admin/Director: todos; Líder: los   ║
// ║  de sus carteras. Sale del último corte (precálculo).                        ║
// ╚══════════════════════════════════════════════════════════════════════════════╝
export function TabAsesores({ alcance, onAlcance, onRegisterDownload }: {
  alcance: Alcance; onAlcance: (a: Alcance) => void; onRegisterDownload?: DownloadRegistrar;
}) {
  const [criterio, setCriterio] = useState<CriterioAsesor>('Contactabilidad');
  const latestRef = useRef<AsesoresLocalizacion | null>(null);

  const { data, isLoading, isError, error } = useQuery({
    queryKey: ['loc-asesores', alcance.liderID, alcance.carteraID, criterio],
    queryFn: () => obtenerAsesoresLocalizacion({ liderID: alcance.liderID, carteraID: alcance.carteraID }, criterio),
    staleTime: 60 * 1000,
  });
  useEffect(() => { latestRef.current = data ?? null; }, [data]);

  useEffect(() => {
    if (!onRegisterDownload) return;
    onRegisterDownload((): PdfExportDescriptor | null => {
      const d = latestRef.current;
      if (!d || d.asesores.length === 0) return null;
      const head = ['#', 'Asesor', etiquetaCriterio(d.criterio), 'Variación', 'Titulares', 'Contactabilidad', `Gestionados ${d.diasGestionReciente} d`, 'Con dato', 'Sin contacto c/ saldo'];
      const body = d.asesores.map(a => [
        a.posicion ?? '—', a.nombre, fmtPct(a.valor),
        a.delta == null ? '—' : `${a.delta > 0 ? '+' : ''}${a.delta.toFixed(1)}`,
        fmtNum(a.titulares), fmtPct(a.pctContactoEfectivo), fmtPct(a.pctGestionadosRecientes), fmtPct(a.pctConDato),
        a.conSaldo > 0 ? `${fmtNum(a.sinContactoConSaldo)} (${fmtPct(a.pctSinContactoConSaldo)})` : '—',
      ]);
      const pdfTable: PdfTableData = { head: [head], body, cumpColIndex: -1, colStyles: { 0: { halign: 'center' }, 1: { halign: 'left' }, 3: { halign: 'center' } } };
      return {
        columnas: head,
        generar: idx => buildExportarPDF({
          tituloArchivo: `Localizacion_Asesores_${d.criterio}`,
          tituloPDF: `Ranking de asesores — ${etiquetaCriterio(d.criterio)}`,
          subtitulo: `${d.etiquetaAlcance} · localización al ${fmtFecha(d.corte.fechaCorte)} · asesores con ${d.minTitulares} titulares o más`,
          pdfTable: filtrarColumnas(pdfTable, idx),
        }),
      };
    });
    return () => onRegisterDownload(null);
  }, [onRegisterDownload]);

  const controles = (
    <div style={{ display: 'flex', gap: 14, flexWrap: 'wrap', alignItems: 'center', justifyContent: 'space-between' }}>
      <FiltrosAlcance alcance={alcance} onChange={onAlcance} />
      <Opciones label="Criterio" opciones={CRITERIOS} valor={criterio} onChange={setCriterio} />
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
  const conAsesor = data.titularesAlcance - data.titularesSinAsesor;

  return (
    <div style={{ display: 'flex', flexDirection: 'column', gap: 20 }}>
      {controles}
      <p style={{ margin: 0, fontSize: 12, color: 'var(--z-text-muted)', lineHeight: 1.5 }}>
        Universo: <strong style={{ color: 'var(--z-text-primary)' }}>{describirAlcance(data.alcance, data.etiquetaAlcance)}</strong>.
        {' '}A cada asesor le cuentan los titulares cuya <strong>última gestión</strong> hizo él ({fmtNum(conAsesor)} de {fmtNum(data.titularesAlcance)};
        {' '}{fmtNum(data.titularesSinAsesor)} no tienen asesor: sin gestión, marcador automático o usuario sin registrar).
      </p>

      {data.aviso && (
        <div style={{ fontSize: 12, color: '#92400e', background: 'rgba(245,158,11,0.10)', border: '1px solid rgba(245,158,11,0.35)', borderRadius: 8, padding: '10px 14px', lineHeight: 1.5 }}>
          {data.aviso}
        </div>
      )}

      {data.asesores.length > 0 && (
        <Tarjeta titulo={`Podio — ${etiquetaCriterio(c)}${data.menorEsMejor ? ' (menos es mejor)' : ''}`}>
          <Podio puestos={data.asesores.map(a => ({
            posicion: a.posicion ?? 99, id: a.documento, nombre: a.nombre, documento: a.documento,
            valor: fmtPct(a.valor), detalle: `${fmtNum(a.titulares)} titulares`,
          }))} />
        </Tarjeta>
      )}

      <Tarjeta
        titulo={`Asesores (${data.asesores.length})`}
        derecha={<span style={{ fontSize: 12, color: 'var(--z-text-muted)' }}>
          Variación contra {data.corteAnterior ? `el corte del ${fmtFecha(data.corteAnterior.fechaCorte)}` : '— (aún no hay un corte de un día anterior)'}
        </span>}
      >
        <TablaAsesores filas={data.asesores} criterio={c} menorEsMejor={data.menorEsMejor} diasGestion={data.diasGestionReciente} />
        <p style={{ margin: '10px 0 0', fontSize: 11, color: 'var(--z-text-muted)', lineHeight: 1.5 }}>
          Contactabilidad = su última gestión fue con contacto (titular, tercero o acuerdo). Gestión reciente = última gestión en
          los últimos {data.diasGestionReciente} días. Con dato = celular o email en alguna fuente de localización. Solo asesores
          activos con {data.minTitulares} titulares o más; empates a un decimal comparten posición.
        </p>
      </Tarjeta>

      {data.pocosTitulares.length > 0 && (
        <details style={{ background: 'var(--z-bg-card)', border: '1px solid var(--z-border)', borderRadius: 10, padding: '14px 16px' }}>
          <summary style={{ cursor: 'pointer', fontSize: 13, fontWeight: 600, color: 'var(--z-text-primary)' }}>
            Con menos de {data.minTitulares} titulares ({data.pocosTitulares.length}): fuera del ranking
          </summary>
          <div style={{ marginTop: 12 }}>
            <TablaAsesores filas={data.pocosTitulares} criterio={c} menorEsMejor={data.menorEsMejor} diasGestion={data.diasGestionReciente} />
          </div>
        </details>
      )}

      <AvisoNoMedible extra={[
        'Los titulares "asignados" a cada asesor: UltimoEstadoTitular.AgenteID es quien hizo la última gestión, no un asignado fijo.',
        'Las gestiones del marcador automático o de usuarios que no están registrados como asesores (no tienen asesor).',
      ]} />
    </div>
  );
}

function TablaAsesores({ filas, criterio, menorEsMejor, diasGestion }: {
  filas: FilaAsesor[]; criterio: CriterioAsesor; menorEsMejor: boolean; diasGestion: number;
}) {
  return (
    <div style={{ overflowX: 'auto' }}>
      <table style={{ width: '100%', borderCollapse: 'collapse', fontSize: 13 }}>
        <thead>
          <tr>
            <th style={{ ...TH, textAlign: 'center', width: 48 }}>#</th>
            <th style={{ ...TH, textAlign: 'left' }}>Asesor</th>
            <th style={{ ...TH, textAlign: 'right' }}>{etiquetaCriterio(criterio)}</th>
            <th style={{ ...TH, textAlign: 'center' }}>Variación</th>
            <th style={{ ...TH, textAlign: 'right' }}>Titulares</th>
            <th style={{ ...TH, textAlign: 'right' }}>Contactabilidad</th>
            <th style={{ ...TH, textAlign: 'right' }}>Gestionados {diasGestion} d</th>
            <th style={{ ...TH, textAlign: 'right' }}>Con dato</th>
            <th style={{ ...TH, textAlign: 'right' }}>Sin contacto c/ saldo</th>
            <th style={{ ...TH, textAlign: 'center' }}>Carteras</th>
          </tr>
        </thead>
        <tbody>
          {filas.map(a => (
            <tr key={a.documento}
              onMouseEnter={e => { e.currentTarget.style.background = 'var(--z-bg-page)'; }}
              onMouseLeave={e => { e.currentTarget.style.background = 'transparent'; }}>
              <td style={{ ...TD, textAlign: 'center', fontWeight: 700, color: 'var(--z-text-primary)' }}>{a.posicion ?? '—'}</td>
              <td style={{ ...TD, fontWeight: 500, color: 'var(--z-text-primary)' }}>
                <span style={{ display: 'inline-flex', alignItems: 'center', gap: 10 }}>
                  <AvatarPersona documento={a.documento} nombre={a.nombre} tam={32} />
                  {a.nombre}
                </span>
              </td>
              <td style={{ ...TD, ...TD_NUM, fontWeight: 700, color: criterio === 'SinContactoSaldo' ? 'var(--z-text-primary)' : colorNivel(a.valor) }}>
                {fmtPct(a.valor)}
              </td>
              <td style={{ ...TD, textAlign: 'center' }}><Delta valor={a.delta} menorEsMejor={menorEsMejor} /></td>
              <td style={{ ...TD, ...TD_NUM }}>{fmtNum(a.titulares)}</td>
              <td style={{ ...TD, ...TD_NUM }}>{fmtPct(a.pctContactoEfectivo)}</td>
              <td style={{ ...TD, ...TD_NUM }}>{fmtPct(a.pctGestionadosRecientes)}</td>
              <td style={{ ...TD, ...TD_NUM }}>{fmtPct(a.pctConDato)}</td>
              <td style={{ ...TD, ...TD_NUM }} title={a.conSaldo > 0 ? cop(a.saldoSinContacto) : undefined}>
                {a.conSaldo > 0
                  ? <>{fmtNum(a.sinContactoConSaldo)} <span style={{ color: 'var(--z-text-muted)', fontSize: 11 }}>({fmtPct(a.pctSinContactoConSaldo)})</span></>
                  : '—'}
              </td>
              <td style={{ ...TD, textAlign: 'center', color: 'var(--z-text-muted)' }}>{a.carteras}</td>
            </tr>
          ))}
        </tbody>
      </table>
    </div>
  );
}
