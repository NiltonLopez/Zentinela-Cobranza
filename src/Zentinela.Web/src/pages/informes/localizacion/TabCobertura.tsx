import { useEffect, useMemo, useRef, useState } from 'react';
import { useQuery } from '@tanstack/react-query';
import { obtenerCoberturaLocalizacion, problemaLocalizacion } from '../../../api/informesLocalizacionApi';
import { descargarLocalizacionCsv, type CategoriaLocalizacion } from '../../../api/calificadorApi';
import type { AgruparCobertura, FilaCobertura } from '../../../types/informesLocalizacion';
import { useAlcanceLider } from '../../../hooks/useAlcanceLider';
import { AvatarPersona } from '../../../components/AvatarPersona';
import { ZDropdown } from '../../../components/ZDropdown';
import {
  Cargando, ErrorMsg, TD, ThSort, buildExportarPDF, filtrarColumnas, useSortable,
} from '../../../components/informes/reporteCompartido';
import type { DownloadRegistrar, PdfExportDescriptor, PdfTableData } from '../../../components/informes/reporteCompartido';
import {
  BarraCobertura, COLOR_LOC, ChipEstadoFuente, FiltrosAlcance, Opciones, TD_NUM, fmtFecha, fmtNum, fmtPct,
  type Alcance,
} from './compartidos';

// Fila plana para ordenar (useSortable ordena por claves de primer nivel).
interface FilaPlana extends Record<string, unknown> {
  key: string;
  grupo: string;
  fuente: string;
  estadoOrden: number;
  buscados: number;
  encontrados: number;
  noEncontrados: number;
  falta: number;
  pctCobertura: number;
  pctExito: number;
  pctLocalizacion: number;
  ultimaConsulta: string;
  ultimoCargue: string;
  original: FilaCobertura;
}

const ORDEN_ESTADO = { Funcional: 0, Intermitente: 1, Inactivo: 2 } as const;

// ╔══════════════════════════════════════════════════════════════════════════════╗
// ║  TAB — COBERTURA POR FUENTE                                                  ║
// ╚══════════════════════════════════════════════════════════════════════════════╝
export function TabCobertura({ alcance, onAlcance, onRegisterDownload }: {
  alcance: Alcance; onAlcance: (a: Alcance) => void; onRegisterDownload?: DownloadRegistrar;
}) {
  const { esLider } = useAlcanceLider();
  const [agrupar, setAgrupar] = useState<AgruparCobertura>('par');
  const [fuente, setFuente] = useState('');
  const [soloFuncionales, setSoloFuncionales] = useState(false);
  const [descargando, setDescargando] = useState<string | null>(null);
  const latestRef = useRef<FilaPlana[]>([]);

  const { data, isLoading, isError, error } = useQuery({
    queryKey: ['loc-cobertura', alcance.liderID, alcance.carteraID, agrupar],
    queryFn: () => obtenerCoberturaLocalizacion({ liderID: alcance.liderID, carteraID: alcance.carteraID }, agrupar),
    staleTime: 60 * 1000,
  });

  const opcionesFuente = useMemo(() => {
    const vistas = new Map<string, string>();
    for (const f of data?.filas ?? []) vistas.set(f.fuente.clave, f.fuente.etiqueta);
    return [...vistas.entries()].map(([value, label]) => ({ value, label }));
  }, [data]);

  const filas = useMemo<FilaPlana[]>(() => (data?.filas ?? [])
    .filter(f => (!fuente || f.fuente.clave === fuente) && (!soloFuncionales || f.fuente.estado === 'Funcional'))
    .map(f => ({
      key: `${f.grupo}|${f.fuente.clave}`,
      grupo: f.etiquetaGrupo,
      fuente: f.fuente.etiqueta,
      estadoOrden: ORDEN_ESTADO[f.fuente.estado] ?? 9,
      buscados: f.fuente.buscados,
      encontrados: f.fuente.encontrados,
      noEncontrados: f.fuente.noEncontrados,
      falta: f.fuente.faltaPorBuscar,
      pctCobertura: f.pctCobertura ?? -1,
      pctExito: f.pctExito ?? -1,
      pctLocalizacion: f.pctLocalizacion ?? -1,
      ultimaConsulta: f.fuente.ultimaConsultaMax ?? '',
      ultimoCargue: f.fuente.ultimoCargueOk ?? '',
      original: f,
    })), [data, fuente, soloFuncionales]);

  const sort = useSortable(filas);
  const ordenadas = sort.sorted as FilaPlana[];
  useEffect(() => { latestRef.current = ordenadas; }, [ordenadas]);

  useEffect(() => {
    if (!onRegisterDownload) return;
    onRegisterDownload((): PdfExportDescriptor | null => {
      const rows = latestRef.current;
      if (rows.length === 0) return null;
      const head = ['Grupo', 'Fuente', 'Estado', 'Buscados', 'Encontrados', 'No encontrados', 'Falta', '% Cobertura', '% Éxito', '% Localización', 'Último dato', 'Último cargue OK'];
      const body = rows.map(r => [
        r.grupo, r.fuente, r.original.fuente.estado,
        fmtNum(r.buscados), fmtNum(r.encontrados), fmtNum(r.noEncontrados), fmtNum(r.falta),
        fmtPct(r.original.pctCobertura), fmtPct(r.original.pctExito), fmtPct(r.original.pctLocalizacion),
        fmtFecha(r.original.fuente.ultimaConsultaMax), fmtFecha(r.original.fuente.ultimoCargueOk),
      ]);
      const pdfTable: PdfTableData = {
        head: [head], body, cumpColIndex: -1,
        colStyles: { 0: { halign: 'left' }, 1: { halign: 'left' }, 2: { halign: 'center' }, 10: { halign: 'center' }, 11: { halign: 'center' } },
      };
      return {
        columnas: head,
        generar: idx => buildExportarPDF({
          tituloArchivo: 'Localizacion_Cobertura_por_Fuente',
          tituloPDF: 'Localización — Cobertura por fuente',
          subtitulo: `Generado el ${new Date().toLocaleDateString('es-CO', { day: 'numeric', month: 'long', year: 'numeric' })}`,
          pdfTable: filtrarColumnas(pdfTable, idx),
        }),
      };
    });
    return () => onRegisterDownload(null);
  }, [onRegisterDownload]);

  // Descarga por celda: el mismo CSV del Calificador (ADR-019) para ese par, fuente y categoría.
  const descargar = async (f: FilaCobertura, categoria: CategoriaLocalizacion) => {
    if (!f.esPar || f.carteraID == null || f.liderID == null || !f.fuente.slug || descargando) return;
    const clave = `${f.grupo}|${f.fuente.clave}|${categoria}`;
    setDescargando(clave);
    try {
      const blob = await descargarLocalizacionCsv(f.carteraID, f.liderID, f.fuente.slug, categoria);
      const url = URL.createObjectURL(blob);
      const a = document.createElement('a');
      a.href = url;
      a.download = `Localizacion_${(f.nombreCartera ?? '').replace(/\s+/g, '_')}_${f.fuente.clave}_${categoria}.csv`;
      document.body.appendChild(a);
      a.click();
      document.body.removeChild(a);
      URL.revokeObjectURL(url);
    } finally {
      setDescargando(null);
    }
  };

  const celda = (f: FilaCobertura, categoria: CategoriaLocalizacion, valor: number, color: string) => {
    const habilitada = f.esPar && valor > 0 && !!f.fuente.slug;
    const enCurso = descargando === `${f.grupo}|${f.fuente.clave}|${categoria}`;
    return (
      <td style={{ ...TD, ...TD_NUM, color }}>
        {habilitada ? (
          <button
            onClick={() => descargar(f, categoria)}
            disabled={!!descargando}
            title="Descargar la lista de titulares (CSV del Calificador)"
            style={{
              background: 'none', border: 'none', padding: 0, color, font: 'inherit',
              cursor: descargando ? 'wait' : 'pointer', textDecoration: 'underline dotted', textUnderlineOffset: 3,
            }}
          >
            {enCurso ? '…' : fmtNum(valor)}
          </button>
        ) : fmtNum(valor)}
      </td>
    );
  };

  const th = (label: string, k: string, align?: 'left' | 'right' | 'center') => (
    <ThSort label={label} k={k} sortKey={sort.sortKey as string | null} sortDir={sort.sortDir} onSort={k2 => sort.handleSort(k2)} align={align} />
  );

  const opcionesAgrupar: { value: AgruparCobertura; label: string }[] = esLider
    ? [{ value: 'par', label: 'Por cartera' }, { value: 'lider', label: 'Todas mis carteras' }]
    : [
        { value: 'par', label: 'Par (líder-cartera)' },
        { value: 'cartera', label: 'Cartera' },
        { value: 'lider', label: alcance.carteraID ? 'Líderes de la cartera' : 'Líder' },
      ];

  return (
    <div style={{ display: 'flex', flexDirection: 'column', gap: 16 }}>
      <FiltrosAlcance alcance={alcance} onChange={onAlcance} />

      <div style={{ display: 'flex', gap: 14, flexWrap: 'wrap', alignItems: 'center' }}>
        <Opciones label="Agrupar" opciones={opcionesAgrupar} valor={agrupar} onChange={setAgrupar} />
        <div style={{ display: 'flex', alignItems: 'center', gap: 6 }}>
          <span style={{ fontSize: 12, color: 'var(--z-text-muted)' }}>Fuente:</span>
          <ZDropdown
            options={[{ value: '', label: 'Todas las fuentes' }, ...opcionesFuente]}
            value={fuente}
            onChange={setFuente}
            style={{ minWidth: 170 }}
          />
        </div>
        <label style={{ display: 'flex', alignItems: 'center', gap: 6, fontSize: 12, color: 'var(--z-text-muted)', cursor: 'pointer' }}>
          <input type="checkbox" checked={soloFuncionales} onChange={e => setSoloFuncionales(e.target.checked)} />
          Solo fuentes Funcionales
        </label>
      </div>

      {isLoading ? <Cargando /> : isError || !data ? (
        problemaLocalizacion(error)
          ? <p style={{ textAlign: 'center', padding: '40px 0', color: 'var(--z-text-muted)', fontSize: 13 }}>{problemaLocalizacion(error)!.mensaje}</p>
          : <ErrorMsg />
      ) : (
        <>
          <p style={{ margin: 0, fontSize: 12, color: 'var(--z-text-muted)' }}>
            {fmtNum(ordenadas.length)} filas. En las filas de un par, las cifras subrayadas descargan la lista de
            titulares (el mismo CSV del Calificador). % Cobertura = buscados / titulares · % Éxito = encontrados /
            buscados · % Localización = encontrados / titulares.
          </p>
          <div style={{ background: 'var(--z-bg-card)', border: '1px solid var(--z-border)', borderRadius: 10, overflow: 'hidden' }}>
            <div style={{ overflowX: 'auto', maxHeight: 640, overflowY: 'auto' }}>
              <table style={{ width: '100%', borderCollapse: 'collapse', fontSize: 13 }}>
                <thead style={{ position: 'sticky', top: 0, zIndex: 1 }}>
                  <tr>
                    {th(agrupar === 'cartera' ? 'Cartera' : agrupar === 'lider' ? 'Líder' : 'Par', 'grupo', 'left')}
                    {th('Fuente', 'fuente', 'left')}
                    {th('Estado', 'estadoOrden', 'center')}
                    {th('Buscados', 'buscados')}
                    {th('Encontrados', 'encontrados')}
                    {th('No enc.', 'noEncontrados')}
                    {th('Falta', 'falta')}
                    {th('% Cob.', 'pctCobertura')}
                    {th('% Éxito', 'pctExito')}
                    {th('% Loc.', 'pctLocalizacion')}
                    <th style={{ padding: '10px 14px', fontSize: 11, fontWeight: 600, textTransform: 'uppercase', background: 'var(--z-brand-light)', borderBottom: '2px solid var(--z-brand)', color: 'var(--z-text-primary)' }}>Distribución</th>
                    {th('Último dato', 'ultimaConsulta', 'center')}
                    {th('Último cargue OK', 'ultimoCargue', 'center')}
                  </tr>
                </thead>
                <tbody>
                  {ordenadas.map(r => {
                    const f = r.original;
                    return (
                      <tr key={r.key}
                        onMouseEnter={e => { e.currentTarget.style.background = 'var(--z-bg-page)'; }}
                        onMouseLeave={e => { e.currentTarget.style.background = 'transparent'; }}>
                        <td style={{ ...TD, fontWeight: 500, color: 'var(--z-text-primary)' }}>
                          <span style={{ display: 'inline-flex', alignItems: 'center', gap: 8 }}>
                            {agrupar === 'lider' && f.liderID != null && (
                              <AvatarPersona documento={f.documentoLider} nombre={f.nombreLider ?? r.grupo} tam={32} />
                            )}
                            {r.grupo}
                          </span>
                        </td>
                        <td style={{ ...TD, color: 'var(--z-text-primary)' }}>{r.fuente}</td>
                        <td style={{ ...TD, textAlign: 'center' }}><ChipEstadoFuente estado={f.fuente.estado} nota={f.fuente.nota} /></td>
                        <td style={{ ...TD, ...TD_NUM }}>{fmtNum(r.buscados)}</td>
                        {celda(f, 'encontrado', r.encontrados, '#15803D')}
                        {celda(f, 'no-encontrado', r.noEncontrados, '#B45309')}
                        {celda(f, 'falta', r.falta, 'var(--z-text-muted)')}
                        <td style={{ ...TD, ...TD_NUM }}>{fmtPct(f.pctCobertura)}</td>
                        <td style={{ ...TD, ...TD_NUM }}>{fmtPct(f.pctExito)}</td>
                        <td style={{ ...TD, ...TD_NUM, fontWeight: 600, color: COLOR_LOC.encontrado }}>{fmtPct(f.pctLocalizacion)}</td>
                        <td style={TD}>
                          <BarraCobertura encontrados={r.encontrados} noEncontrados={r.noEncontrados} falta={r.falta} />
                        </td>
                        <td style={{ ...TD, textAlign: 'center', color: 'var(--z-text-muted)' }}>
                          {fmtFecha(f.fuente.ultimaConsultaMax)}
                        </td>
                        <td style={{ ...TD, textAlign: 'center', color: 'var(--z-text-muted)' }}>
                          {f.fuente.ultimoCargueOk ? fmtFecha(f.fuente.ultimoCargueOk) : '—'}
                        </td>
                      </tr>
                    );
                  })}
                </tbody>
              </table>
            </div>
          </div>
        </>
      )}
    </div>
  );
}
