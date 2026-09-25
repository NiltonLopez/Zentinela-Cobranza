import { useEffect, useMemo, useRef, useState } from 'react';
import { Link } from 'react-router-dom';
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query';
import { useAuth } from '../../../context/AuthContext';
import { useAlcanceLider } from '../../../hooks/useAlcanceLider';
import {
  cambiarEstadoEnvio, descargarEnvioExcel, listarEnviosLocalizacion, obtenerEnvioLocalizacion,
  obtenerUsoDepartamento, problemaEnvios, problemaLocalizacion,
} from '../../../api/informesLocalizacionApi';
import { obtenerFuentesLocalizacion } from '../../../api/fuentesLocalizacionApi';
import type {
  AccionLote, EnvioLocalizacion, EstadoLote, FiltroEnvios, FilaUsoLider, UsoDepartamento,
} from '../../../types/informesLocalizacion';
import { AvatarPersona } from '../../../components/AvatarPersona';
import { ZDropdown } from '../../../components/ZDropdown';
import {
  C, Cargando, ErrorMsg, KpiCard, TD, TH, buildExportarPDF, cop, filtrarColumnas,
} from '../../../components/informes/reporteCompartido';
import type { DownloadRegistrar, PdfExportDescriptor, PdfTableData } from '../../../components/informes/reporteCompartido';
import {
  AvisoNoMedible, ChipEstadoLote, FiltrosAlcance, ModalConfirmar, Opciones, TD_NUM, Tarjeta,
  fmtFecha, fmtFechaHora, fmtNum, fmtPct, type Alcance,
} from './compartidos';

const ESTADOS: { value: EstadoLote | ''; label: string }[] = [
  { value: '',         label: 'Todos' },
  { value: 'BORRADOR', label: 'Borrador' },
  { value: 'ENVIADO',  label: 'Enviado' },
  { value: 'CERRADO',  label: 'Cerrado' },
  { value: 'ANULADO',  label: 'Anulado' },
];

const CATEGORIA: Record<string, string> = {
  NUNCA_BUSCADO: 'Nunca buscado',
  NO_ENC_VIEJO:  'No enc. y viejo',
  ENC_VIEJO:     'Enc. pero viejo',
};

const NO_MEDIBLE_ENVIOS = [
  'Qué archivo devolvió cada lote: el retorno es el primer cargue OK de la fuente después del envío (si dos lotes de la misma fuente esperan, los dos lo toman).',
  'Gestiones y pagos del mismo día del envío: UltimoEstadoTitular y Pago guardan solo la fecha; se cuentan los de días posteriores.',
  'La cola sugerida exacta de cada líder: se aproxima con el último corte (un techo, antes de exclusiones), así que el % enviado es un piso.',
];

function guardarBlob(blob: Blob, nombre: string) {
  const url = URL.createObjectURL(blob);
  const a = document.createElement('a');
  a.href = url;
  a.download = nombre;
  document.body.appendChild(a);
  a.click();
  document.body.removeChild(a);
  URL.revokeObjectURL(url);
}

/** "2026-09-25" en la zona del navegador. */
const isoDia = (d: Date) =>
  `${d.getFullYear()}-${String(d.getMonth() + 1).padStart(2, '0')}-${String(d.getDate()).padStart(2, '0')}`;

/** Resumen corto del retorno de un lote para la lista. */
function textoRetorno(e: EnvioLocalizacion): string {
  const r = e.retorno;
  if (!e.fechaEnvio || !r) return '—';
  if (!r.fechaRetorno) return 'esperando';
  return `${fmtPct(r.pctRetorno)} · ${r.diasRetorno ?? 0} d`;
}

function AvisoSinTablas({ mensaje }: { mensaje: string }) {
  return (
    <div style={{
      background: 'var(--z-bg-card)', border: '1px solid var(--z-border)', borderLeft: '4px solid #dc2626',
      borderRadius: 10, padding: '20px 22px', display: 'flex', flexDirection: 'column', gap: 6,
    }}>
      <p style={{ margin: 0, fontSize: 15, fontWeight: 600, color: 'var(--z-text-primary)' }}>El registro de envíos todavía no está instalado</p>
      <p style={{ margin: 0, fontSize: 13, color: 'var(--z-text-muted)', lineHeight: 1.5 }}>
        {mensaje || 'Falta aplicar docs/sql/migrations/2026-09-26_informes_localizacion.sql.'} Un administrador de la base de
        datos debe ejecutar ese script; mientras tanto no se pueden crear ni consultar lotes.
      </p>
    </div>
  );
}

// ╔══════════════════════════════════════════════════════════════════════════════╗
// ║  TAB — ENVÍOS A LOCALIZACIÓN (etapa 3)                                        ║
// ║  Lotes que los líderes mandan a buscar, su retorno e impacto, y el uso del    ║
// ║  departamento por líder. No depende del corte: lee LocalizacionEnvio*.        ║
// ╚══════════════════════════════════════════════════════════════════════════════╝
export function TabEnvios({ alcance, onAlcance, onRegisterDownload, loteInicial }: {
  alcance: Alcance; onAlcance: (a: Alcance) => void; onRegisterDownload?: DownloadRegistrar;
  /** Lote a abrir al entrar (el recién creado desde Sugerencias). */
  loteInicial?: number | null;
}) {
  const [fuente, setFuente] = useState('');
  const [estado, setEstado] = useState<EstadoLote | ''>('');
  const [seleccion, setSeleccion] = useState<number | null>(loteInicial ?? null);
  const [loteVisto, setLoteVisto] = useState(loteInicial);
  // Un lote nuevo creado desde Sugerencias se abre al llegar (ajuste de estado durante el render, sin efecto).
  if (loteInicial !== loteVisto) {
    setLoteVisto(loteInicial);
    if (loteInicial) setSeleccion(loteInicial);
  }

  const { data: catalogo = [] } = useQuery({
    queryKey: ['fuentes-localizacion'],
    queryFn: obtenerFuentesLocalizacion,
    staleTime: 10 * 60 * 1000,
  });
  const opcionesFuente = useMemo(() => [
    { value: '', label: 'Todas las fuentes' },
    ...catalogo.filter(f => f.enPanelCalificador && f.estado === 'Funcional').map(f => ({ value: f.clave, label: f.etiquetaCalificador })),
  ], [catalogo]);

  const filtro: FiltroEnvios = {
    ...(alcance.liderID ? { liderID: alcance.liderID } : {}),
    ...(alcance.carteraID ? { carteraID: alcance.carteraID } : {}),
    ...(fuente ? { fuente } : {}),
    ...(estado ? { estado } : {}),
  };
  const lista = useQuery({
    queryKey: ['loc-envios', filtro],
    queryFn: () => listarEnviosLocalizacion(filtro),
    staleTime: 30 * 1000,
    retry: (n, err) => problemaLocalizacion(err) == null && n < 2,
  });
  const lotes = useMemo(() => lista.data?.envios ?? [], [lista.data]);

  const latestRef = useRef<EnvioLocalizacion[]>([]);
  useEffect(() => { latestRef.current = lotes; }, [lotes]);

  // PDF de la lista de lotes.
  useEffect(() => {
    if (!onRegisterDownload) return;
    onRegisterDownload((): PdfExportDescriptor | null => {
      const d = latestRef.current;
      if (d.length === 0) return null;
      const head = ['Lote', 'Líder', 'Cartera', 'Fuente', 'Estado', 'Titulares', 'Creado', 'Enviado', 'Buscados', '% retorno', 'Localizados', 'Días', 'Gestionados', 'Pagaron', 'Valor pagado'];
      const body = d.map(e => [
        e.codigo, e.lider, e.cartera, e.etiquetaFuente, e.estado, fmtNum(e.titulares), fmtFecha(e.fechaCreacion), fmtFecha(e.fechaEnvio),
        e.retorno ? fmtNum(e.retorno.buscados) : '—', e.retorno ? fmtPct(e.retorno.pctRetorno) : '—',
        e.retorno ? fmtNum(e.retorno.localizados) : '—', e.retorno?.diasRetorno != null ? String(e.retorno.diasRetorno) : '—',
        e.impacto ? fmtNum(e.impacto.gestionados) : '—', e.impacto ? fmtNum(e.impacto.titularesConPago) : '—',
        e.impacto ? cop(e.impacto.valorPagado) : '—',
      ]);
      const pdfTable: PdfTableData = {
        head: [head], body, cumpColIndex: -1,
        colStyles: { 0: { halign: 'left' }, 1: { halign: 'left' }, 2: { halign: 'left' }, 3: { halign: 'left' }, 4: { halign: 'center' } },
      };
      return {
        columnas: head,
        generar: idx => buildExportarPDF({
          tituloArchivo: 'Localizacion_Envios',
          tituloPDF: 'Envíos a localización',
          subtitulo: `${fmtNum(d.length)} lotes · retorno = primer cargue OK de la fuente después del envío`,
          pdfTable: filtrarColumnas(pdfTable, idx),
        }),
      };
    });
    return () => onRegisterDownload(null);
  }, [onRegisterDownload]);

  const sinTablas = problemaLocalizacion(lista.error)?.tipo === 'sin-tablas';
  if (sinTablas) return <AvisoSinTablas mensaje={problemaLocalizacion(lista.error)?.mensaje ?? ''} />;

  const enviados = lotes.filter(e => e.fechaEnvio && e.estado !== 'ANULADO');
  const titularesEnviados = enviados.reduce((s, e) => s + e.titulares, 0);
  const buscados = enviados.reduce((s, e) => s + (e.retorno?.buscados ?? 0), 0);
  const localizados = enviados.reduce((s, e) => s + (e.retorno?.localizados ?? 0), 0);
  const borradores = lotes.filter(e => e.estado === 'BORRADOR').length;

  return (
    <div style={{ display: 'flex', flexDirection: 'column', gap: 20 }}>
      <Tarjeta titulo="Lotes de envío a localización">
        <div style={{ display: 'flex', flexDirection: 'column', gap: 12 }}>
          <FiltrosAlcance alcance={alcance} onChange={onAlcance} />
          <div style={{ display: 'flex', gap: 18, flexWrap: 'wrap', alignItems: 'center' }}>
            <div style={{ display: 'flex', alignItems: 'center', gap: 6 }}>
              <span style={{ fontSize: 12, color: 'var(--z-text-muted)' }}>Fuente:</span>
              <ZDropdown ariaLabel="Fuente" options={opcionesFuente} value={fuente} onChange={setFuente} style={{ minWidth: 180 }} />
            </div>
            <Opciones<EstadoLote | ''> label="Estado" opciones={ESTADOS} valor={estado} onChange={setEstado} />
          </div>
          <p style={{ margin: 0, fontSize: 12, color: 'var(--z-text-muted)' }}>
            Los lotes se crean desde la pestaña Sugerencias (&quot;Crear lote de envío&quot;). Un lote nace en borrador; al
            enviarlo empieza a contar su retorno: el primer cargue OK de su fuente en el Cargue de Fuentes.
          </p>
        </div>
      </Tarjeta>

      {lista.isLoading ? <Cargando /> : lista.isError ? <ErrorMsg /> : (
        <>
          <div style={{ display: 'flex', gap: 12, flexWrap: 'wrap' }}>
            <KpiCard label="Lotes" value={fmtNum(lotes.length)} sub={borradores > 0 ? `${fmtNum(borradores)} en borrador` : undefined} />
            <KpiCard label="Titulares enviados" value={fmtNum(titularesEnviados)} sub={`en ${fmtNum(enviados.length)} lotes enviados`} color={C.brand} />
            <KpiCard label="Buscados tras el envío" value={fmtNum(buscados)}
              sub={titularesEnviados > 0 ? `${fmtPct((buscados * 100) / titularesEnviados)} de retorno` : undefined} />
            <KpiCard label="Localizados" value={fmtNum(localizados)}
              sub={buscados > 0 ? `${fmtPct((localizados * 100) / buscados)} de los buscados` : undefined} color="#16a34a" />
          </div>

          <TablaLotes lotes={lotes} recortado={!!lista.data?.recortado} seleccion={seleccion} onSeleccion={setSeleccion} />

          {seleccion != null && <DetalleLote id={seleccion} onCerrar={() => setSeleccion(null)} />}
        </>
      )}

      <UsoDelDepartamento liderID={alcance.liderID} />

      <AvisoNoMedible extra={NO_MEDIBLE_ENVIOS} />
    </div>
  );
}

// ── Lista de lotes ────────────────────────────────────────────────────────────

function TablaLotes({ lotes, recortado, seleccion, onSeleccion }: {
  lotes: EnvioLocalizacion[]; recortado: boolean; seleccion: number | null; onSeleccion: (id: number) => void;
}) {
  return (
    <div style={{ background: 'var(--z-bg-card)', border: '1px solid var(--z-border)', borderRadius: 10, overflow: 'hidden' }}>
      {lotes.length === 0 ? (
        <p style={{ margin: 0, textAlign: 'center', padding: '32px 0', fontSize: 13, color: 'var(--z-text-muted)' }}>
          No hay lotes con estos filtros.
        </p>
      ) : (
        <div style={{ overflowX: 'auto' }}>
          <table style={{ width: '100%', borderCollapse: 'collapse', fontSize: 13 }}>
            <thead>
              <tr>
                <th style={{ ...TH, textAlign: 'left' }}>Lote</th>
                <th style={{ ...TH, textAlign: 'left' }}>Líder</th>
                <th style={{ ...TH, textAlign: 'left' }}>Cartera</th>
                <th style={{ ...TH, textAlign: 'left' }}>Fuente</th>
                <th style={{ ...TH, textAlign: 'center' }}>Estado</th>
                <th style={{ ...TH, textAlign: 'right' }}>Titulares</th>
                <th style={{ ...TH, textAlign: 'center' }}>Creado</th>
                <th style={{ ...TH, textAlign: 'center' }}>Enviado</th>
                <th style={{ ...TH, textAlign: 'right' }} title="Buscados tras el envío / titulares · días hasta el retorno">Retorno</th>
                <th style={{ ...TH, textAlign: 'right' }}>Localizados</th>
                <th style={{ ...TH, textAlign: 'right' }}>Gestionados</th>
                <th style={{ ...TH, textAlign: 'right' }}>Pagos después</th>
              </tr>
            </thead>
            <tbody>
              {lotes.map(e => {
                const activo = e.envioID === seleccion;
                return (
                  <tr key={e.envioID} onClick={() => onSeleccion(e.envioID)}
                    style={{ cursor: 'pointer', background: activo ? 'var(--z-brand-light)' : 'transparent' }}
                    onMouseEnter={ev => { if (!activo) ev.currentTarget.style.background = 'var(--z-bg-page)'; }}
                    onMouseLeave={ev => { if (!activo) ev.currentTarget.style.background = 'transparent'; }}>
                    <td style={{ ...TD, fontWeight: 600, color: 'var(--z-text-primary)', fontVariantNumeric: 'tabular-nums' }}>
                      <button type="button" onClick={() => onSeleccion(e.envioID)} aria-label={`Ver el lote ${e.codigo}`} style={{
                        background: 'none', border: 'none', padding: 0, font: 'inherit', color: 'inherit', cursor: 'pointer',
                      }}>{e.codigo}</button>
                    </td>
                    <td style={TD}>
                      <span style={{ display: 'inline-flex', alignItems: 'center', gap: 8 }}>
                        <AvatarPersona documento={e.documentoLider} nombre={e.lider} tam={24} />
                        <span style={{ color: 'var(--z-text-primary)' }}>{e.lider}</span>
                      </span>
                    </td>
                    <td style={TD}>{e.cartera}</td>
                    <td style={TD}>{e.etiquetaFuente}</td>
                    <td style={{ ...TD, textAlign: 'center' }}><ChipEstadoLote estado={e.estado} /></td>
                    <td style={{ ...TD, ...TD_NUM }}>{fmtNum(e.titulares)}</td>
                    <td style={{ ...TD, textAlign: 'center', color: 'var(--z-text-muted)' }} title={`por ${e.creadoPor}`}>{fmtFecha(e.fechaCreacion)}</td>
                    <td style={{ ...TD, textAlign: 'center', color: 'var(--z-text-muted)' }} title={e.enviadoPor ? `por ${e.enviadoPor}` : undefined}>{fmtFecha(e.fechaEnvio)}</td>
                    <td style={{ ...TD, ...TD_NUM }}>{textoRetorno(e)}</td>
                    <td style={{ ...TD, ...TD_NUM }}>{e.retorno ? fmtNum(e.retorno.localizados) : '—'}</td>
                    <td style={{ ...TD, ...TD_NUM }}>{e.impacto ? fmtNum(e.impacto.gestionados) : '—'}</td>
                    <td style={{ ...TD, ...TD_NUM }} title={e.impacto ? `${fmtNum(e.impacto.titularesConPago)} titulares` : undefined}>
                      {e.impacto ? cop(e.impacto.valorPagado) : '—'}
                    </td>
                  </tr>
                );
              })}
            </tbody>
          </table>
        </div>
      )}
      {recortado && (
        <p style={{ margin: 0, padding: '8px 14px', fontSize: 11, color: 'var(--z-text-muted)', borderTop: '1px solid var(--z-border)' }}>
          Se muestran los lotes más recientes; usa los filtros para ver los anteriores.
        </p>
      )}
    </div>
  );
}

// ── Detalle de un lote ────────────────────────────────────────────────────────

const TAM_PAGINA = 50;

const ACCIONES: Record<AccionLote, { titulo: string; boton: string; color: string; texto: (e: EnvioLocalizacion) => string }> = {
  enviar: {
    titulo: 'Enviar el lote', boton: 'Marcar como enviado', color: '#2563eb',
    texto: e => `El lote ${e.codigo} (${fmtNum(e.titulares)} titulares, ${e.etiquetaFuente}) queda como ENVIADO desde este momento: `
      + 'a partir de aquí se mide su retorno. Descarga el Excel y entrégaselo al departamento de localización.',
  },
  cerrar: {
    titulo: 'Cerrar el lote', boton: 'Cerrar', color: '#16a34a',
    texto: e => `El lote ${e.codigo} queda CERRADO: el departamento ya lo devolvió. El retorno y el impacto se siguen calculando.`,
  },
  anular: {
    titulo: 'Anular el lote', boton: 'Anular', color: '#dc2626',
    texto: e => `El lote ${e.codigo} queda ANULADO y deja de contar en el uso del departamento. No se puede deshacer.`,
  },
};

function DetalleLote({ id, onCerrar }: { id: number; onCerrar: () => void }) {
  const { usuario } = useAuth();
  const puedeCargarRetorno = usuario?.rol === 'Admin' || usuario?.rol === 'Auxiliar de Sistemas';
  const queryClient = useQueryClient();
  const [pagina, setPagina] = useState(1);
  const [accion, setAccion] = useState<AccionLote | null>(null);
  const [motivo, setMotivo] = useState('');
  const [errorAccion, setErrorAccion] = useState<string | null>(null);
  const [descargando, setDescargando] = useState(false);
  const [errorExcel, setErrorExcel] = useState<string | null>(null);
  const [idVisto, setIdVisto] = useState(id);
  if (id !== idVisto) {
    setIdVisto(id);
    setPagina(1);
    setAccion(null);
  }

  const q = useQuery({
    queryKey: ['loc-envio', id, pagina],
    queryFn: () => obtenerEnvioLocalizacion(id, pagina, TAM_PAGINA),
    staleTime: 30 * 1000,
    placeholderData: prev => (prev?.envio.envioID === id ? prev : undefined),
  });

  const mutacion = useMutation({
    mutationFn: (a: AccionLote) => cambiarEstadoEnvio(id, a, motivo),
    onSuccess: () => {
      setAccion(null);
      setMotivo('');
      void queryClient.invalidateQueries({ queryKey: ['loc-envios'] });
      void queryClient.invalidateQueries({ queryKey: ['loc-envio', id] });
      void queryClient.invalidateQueries({ queryKey: ['loc-uso'] });
    },
    onError: async err => {
      const p = await problemaEnvios(err);
      setErrorAccion(p.mensaje);
      // 409: otro usuario lo cambió; se recarga para mostrar el estado real.
      if (p.conflicto) void queryClient.invalidateQueries({ queryKey: ['loc-envio', id] });
    },
  });

  const excel = async (e: EnvioLocalizacion) => {
    setDescargando(true);
    setErrorExcel(null);
    try {
      guardarBlob(await descargarEnvioExcel(e.envioID), `Localizacion_${e.fuente}_${e.codigo}.xlsx`);
    } catch (err) {
      setErrorExcel((await problemaEnvios(err)).mensaje);
    } finally {
      setDescargando(false);
    }
  };

  if (q.isLoading) return <Cargando />;
  if (q.isError || !q.data) {
    return (
      <Tarjeta titulo="Lote" derecha={<button type="button" onClick={onCerrar} style={botonSecundario}>Cerrar</button>}>
        <p style={{ margin: 0, fontSize: 13, color: 'var(--z-text-muted)' }}>No se pudo cargar el lote (no existe o no tienes acceso a él).</p>
      </Tarjeta>
    );
  }

  const d = q.data;
  const e = d.envio;
  const r = e.retorno;
  const i = e.impacto;
  const paginas = Math.max(1, Math.ceil(d.totalFilas / d.tamPagina));
  const abrir = (a: AccionLote) => { setErrorAccion(null); setAccion(a); };

  return (
    <Tarjeta
      titulo={
        <span style={{ display: 'inline-flex', alignItems: 'center', gap: 10, flexWrap: 'wrap' }}>
          {e.codigo} · {e.etiquetaFuente} · {e.cartera} — {e.lider}
          <ChipEstadoLote estado={e.estado} />
        </span>
      }
      derecha={
        <div style={{ display: 'flex', gap: 8, flexWrap: 'wrap', alignItems: 'center' }}>
          {e.estado === 'BORRADOR' && <button type="button" onClick={() => abrir('enviar')} style={{ ...botonPrimario, background: '#2563eb' }}>Enviar</button>}
          {e.estado === 'ENVIADO' && <button type="button" onClick={() => abrir('cerrar')} style={{ ...botonPrimario, background: '#16a34a' }}>Cerrar</button>}
          {(e.estado === 'BORRADOR' || e.estado === 'ENVIADO') && (
            <button type="button" onClick={() => abrir('anular')} style={{ ...botonSecundario, color: '#dc2626', borderColor: '#dc262655' }}>Anular</button>
          )}
          <button type="button" onClick={() => void excel(e)} disabled={descargando}
            title="Formato del Cargue de Fuentes, con los titulares del lote"
            style={{ ...botonPrimario, background: descargando ? '#94a3b8' : '#0f766e' }}>
            {descargando ? 'Generando…' : '↓ Excel del lote'}
          </button>
          {puedeCargarRetorno && e.estado === 'ENVIADO' && (
            <Link to="/administracion/fuentes-datos" style={{ ...botonSecundario, textDecoration: 'none' }}
              title="Cuando el departamento devuelva el archivo lleno, se sube en Cargue de Fuentes de Datos">
              Cargar retorno →
            </Link>
          )}
          <button type="button" onClick={onCerrar} style={botonSecundario} aria-label="Cerrar el detalle">×</button>
        </div>
      }
    >
      <div style={{ display: 'flex', flexDirection: 'column', gap: 16 }}>
        {errorExcel && <p style={{ margin: 0, fontSize: 12, color: '#dc2626' }}>{errorExcel}</p>}

        {/* Línea de tiempo */}
        <div style={{ display: 'flex', gap: 18, flexWrap: 'wrap', fontSize: 12, color: 'var(--z-text-muted)' }}>
          <span>Creado {fmtFechaHora(e.fechaCreacion)} por <strong style={{ color: 'var(--z-text-primary)' }}>{e.creadoPor}</strong></span>
          {e.fechaEnvio && <span>Enviado {fmtFechaHora(e.fechaEnvio)} por <strong style={{ color: 'var(--z-text-primary)' }}>{e.enviadoPor}</strong></span>}
          {e.fechaCierre && <span>Cerrado {fmtFechaHora(e.fechaCierre)} por <strong style={{ color: 'var(--z-text-primary)' }}>{e.cerradoPor}</strong></span>}
          {e.fechaAnulacion && (
            <span>Anulado {fmtFechaHora(e.fechaAnulacion)} por <strong style={{ color: 'var(--z-text-primary)' }}>{e.anuladoPor}</strong>
              {e.motivoAnulacion ? ` — ${e.motivoAnulacion}` : ''}</span>
          )}
          {e.observacion && <span>Nota: {e.observacion}</span>}
        </div>

        {/* Retorno e impacto */}
        {!e.fechaEnvio ? (
          <p style={{ margin: 0, fontSize: 13, color: 'var(--z-text-muted)' }}>
            {e.estado === 'ANULADO' ? 'Lote anulado sin enviarse.' : 'Todavía no se ha enviado: el retorno y el impacto se miden desde el envío.'}
          </p>
        ) : r && i && (
          <>
            <div style={{ display: 'flex', gap: 12, flexWrap: 'wrap' }}>
              <KpiCard label="Buscados tras el envío" value={fmtNum(r.buscados)} sub={`${fmtPct(r.pctRetorno)} de ${fmtNum(e.titulares)} · retorno`} color={C.brand} />
              <KpiCard label="Localizados" value={fmtNum(r.localizados)}
                sub={r.buscados > 0 ? `${fmtPct(r.pctLocalizadosBuscados)} de los buscados` : 'sin buscados todavía'} color="#16a34a" />
              <KpiCard label="Días al retorno" value={r.diasRetorno != null ? fmtNum(r.diasRetorno) : '—'}
                sub={r.fechaRetorno ? fmtFecha(r.fechaRetorno) : 'esperando el retorno'} />
            </div>
            <p style={{ margin: 0, fontSize: 12, color: 'var(--z-text-muted)', lineHeight: 1.5 }}>
              {r.base === 'CARGUE'
                ? `Retorno: cargue #${r.cargueEjecucionID} de ${e.etiquetaFuente} del ${fmtFechaHora(r.fechaCargue)} (primer cargue OK después del envío). Cuenta como buscado quien tiene un dato desde el ${fmtFecha(r.buscadosDesde)}.`
                : `Todavía no hay un cargue OK de ${e.etiquetaFuente} después del envío: cuenta como buscado quien tenga un dato posterior al ${fmtFechaHora(r.buscadosDesde)}.`}
            </p>
            <div style={{ display: 'flex', gap: 12, flexWrap: 'wrap' }}>
              <KpiCard label="Gestionados después" value={fmtNum(i.gestionados)} sub={`${fmtPct(i.pctGestionados)} del lote`} />
              <KpiCard label="Pasaron a contacto" value={fmtNum(i.pasaronAContacto)} sub="perfil sin contacto → con contacto" />
              <KpiCard label="Pagaron después" value={fmtNum(i.titularesConPago)}
                sub={`${cop(i.valorPagado)} · honorarios ${cop(i.honorarios)}`} color={i.titularesConPago > 0 ? '#16a34a' : undefined} />
              <KpiCard label="Promesas vigentes nuevas" value={fmtNum(i.promesasVigentes)} />
            </div>
          </>
        )}

        {/* Detalle */}
        <div style={{ overflowX: 'auto', margin: '0 -16px' }}>
          <table style={{ width: '100%', borderCollapse: 'collapse', fontSize: 12 }}>
            <thead>
              <tr>
                <th style={{ ...TH, textAlign: 'center' }}>Prioridad</th>
                <th style={{ ...TH, textAlign: 'left' }}>Titular</th>
                <th style={{ ...TH, textAlign: 'left' }}>Nombre</th>
                <th style={{ ...TH, textAlign: 'left' }}>Categoría</th>
                <th style={{ ...TH, textAlign: 'center' }}>Consulta previa</th>
                <th style={{ ...TH, textAlign: 'right' }}>Saldo (envío)</th>
                <th style={{ ...TH, textAlign: 'left' }}>Perfil al enviar → hoy</th>
                <th style={{ ...TH, textAlign: 'center' }}>Dato nuevo</th>
                <th style={{ ...TH, textAlign: 'center' }}>Gestión</th>
                <th style={{ ...TH, textAlign: 'right' }}>Pagó</th>
                <th style={{ ...TH, textAlign: 'center' }}>Promesa</th>
              </tr>
            </thead>
            <tbody>
              {d.filas.map(f => (
                <tr key={f.titularID}>
                  <td style={{ ...TD, textAlign: 'center', fontWeight: 600 }}>{f.prioridad}</td>
                  <td style={{ ...TD, fontVariantNumeric: 'tabular-nums' }}>{f.titularID}</td>
                  <td style={{ ...TD, color: 'var(--z-text-primary)', maxWidth: 200, overflow: 'hidden', textOverflow: 'ellipsis', whiteSpace: 'nowrap' }} title={f.nombre ?? undefined}>{f.nombre ?? '—'}</td>
                  <td style={TD}>{CATEGORIA[f.categoria] ?? f.categoria}</td>
                  <td style={{ ...TD, textAlign: 'center', color: 'var(--z-text-muted)' }}>{f.ultimaConsultaPrevia ? fmtFecha(f.ultimaConsultaPrevia) : 'nunca'}</td>
                  <td style={{ ...TD, ...TD_NUM }}>{f.saldoFoto != null && f.saldoFoto > 0 ? cop(f.saldoFoto) : '—'}</td>
                  <td style={{ ...TD, color: 'var(--z-text-muted)' }}>
                    {f.perfilFoto ?? '—'}
                    {f.gestionado && <> → <span style={{ color: f.pasoAContacto ? '#16a34a' : 'var(--z-text-primary)', fontWeight: f.pasoAContacto ? 600 : 400 }}>{f.perfilActual ?? '—'}</span></>}
                  </td>
                  <td style={{ ...TD, textAlign: 'center' }}>
                    {!f.buscado ? <span style={{ color: 'var(--z-text-muted)' }}>—</span>
                      : <span style={{ color: f.localizado ? '#16a34a' : '#F59E0B', fontWeight: 600 }} title={fmtFecha(f.fechaDato)}>
                          {f.localizado ? 'Localizado' : 'No encontrado'}
                        </span>}
                  </td>
                  <td style={{ ...TD, textAlign: 'center', color: 'var(--z-text-muted)' }}>
                    {f.gestionado ? fmtFecha(f.gestionActual) : '—'}
                  </td>
                  <td style={{ ...TD, ...TD_NUM }}>{f.valorPagado > 0 ? cop(f.valorPagado) : '—'}</td>
                  <td style={{ ...TD, textAlign: 'center', color: 'var(--z-text-muted)' }}>{f.promesaVigente ? fmtFecha(f.promesaVigente) : '—'}</td>
                </tr>
              ))}
            </tbody>
          </table>
        </div>
        {paginas > 1 && (
          <div style={{ display: 'flex', justifyContent: 'flex-end', alignItems: 'center', gap: 10, fontSize: 12, color: 'var(--z-text-muted)' }}>
            <button type="button" disabled={pagina <= 1} onClick={() => setPagina(p => p - 1)} style={botonSecundario}>‹ Anterior</button>
            Página {pagina} de {paginas} ({fmtNum(d.totalFilas)} titulares)
            <button type="button" disabled={pagina >= paginas} onClick={() => setPagina(p => p + 1)} style={botonSecundario}>Siguiente ›</button>
          </div>
        )}
      </div>

      {accion && (
        <ModalConfirmar
          titulo={ACCIONES[accion].titulo}
          texto={ACCIONES[accion].texto(e)}
          confirmar={ACCIONES[accion].boton}
          color={ACCIONES[accion].color}
          ocupado={mutacion.isPending}
          error={errorAccion}
          onConfirmar={() => mutacion.mutate(accion)}
          onCerrar={() => setAccion(null)}
        >
          {accion === 'anular' && (
            <label style={{ display: 'flex', flexDirection: 'column', gap: 4, fontSize: 12, color: 'var(--z-text-muted)' }}>
              Motivo (opcional)
              <textarea value={motivo} onChange={ev => setMotivo(ev.target.value.slice(0, 300))} rows={3} style={{
                resize: 'vertical', padding: 8, borderRadius: 6, border: '1px solid var(--z-border)',
                background: 'var(--z-bg-page)', color: 'var(--z-text-primary)', fontSize: 13, fontFamily: 'inherit',
              }} />
            </label>
          )}
        </ModalConfirmar>
      )}
    </Tarjeta>
  );
}

const botonPrimario: React.CSSProperties = {
  padding: '6px 14px', borderRadius: 7, fontSize: 13, fontWeight: 600, border: 'none', color: '#fff', cursor: 'pointer', whiteSpace: 'nowrap',
};

const botonSecundario: React.CSSProperties = {
  padding: '6px 12px', borderRadius: 7, fontSize: 13, fontWeight: 500, border: '1px solid var(--z-border)',
  background: 'transparent', color: 'var(--z-text-muted)', cursor: 'pointer', whiteSpace: 'nowrap',
};

// ── Uso del departamento por líder ────────────────────────────────────────────

const PERIODOS = [
  { value: 30, label: '30 d' },
  { value: 90, label: '90 d' },
  { value: 180, label: '180 d' },
  { value: 365, label: '1 año' },
];

function UsoDelDepartamento({ liderID }: { liderID: number }) {
  const { esLider } = useAlcanceLider();
  const [dias, setDias] = useState(90);
  // "Hoy" se fija al montar la sección (el render tiene que ser puro).
  const [hoy] = useState(() => new Date());
  const hasta = isoDia(hoy);
  const desde = isoDia(new Date(hoy.getFullYear(), hoy.getMonth(), hoy.getDate() - dias));

  const q = useQuery({
    queryKey: ['loc-uso', desde, hasta, liderID],
    queryFn: () => obtenerUsoDepartamento(desde, hasta, liderID || undefined),
    staleTime: 60 * 1000,
    retry: (n, err) => problemaLocalizacion(err) == null && n < 2,
  });

  return (
    <Tarjeta
      titulo={esLider ? 'Tu uso del departamento de localización' : 'Uso del departamento de localización por líder'}
      derecha={<Opciones<number> label="Lotes enviados en los últimos" opciones={PERIODOS} valor={dias} onChange={setDias} />}
    >
      {q.isLoading ? <Cargando /> : q.isError || !q.data
        ? (problemaLocalizacion(q.error)?.tipo === 'sin-tablas'
            ? <p style={{ margin: 0, fontSize: 13, color: 'var(--z-text-muted)' }}>Falta aplicar el script de las tablas de envíos.</p>
            : <ErrorMsg />)
        : <TablaUso uso={q.data} />}
    </Tarjeta>
  );
}

function TablaUso({ uso }: { uso: UsoDepartamento }) {
  const fila = (l: FilaUsoLider, total = false) => (
    <tr key={total ? 'total' : l.liderID} style={total ? { background: 'var(--z-bg-page)', fontWeight: 600 } : undefined}>
      <td style={{ ...TD, color: 'var(--z-text-primary)' }}>
        {total ? 'Total' : (
          <span style={{ display: 'inline-flex', alignItems: 'center', gap: 10 }}>
            <AvatarPersona documento={l.documento} nombre={l.nombre} tam={32} />
            {l.nombre}
          </span>
        )}
      </td>
      <td style={{ ...TD, ...TD_NUM }}>{fmtNum(l.lotes)}{l.borradores > 0 && <span style={{ color: 'var(--z-text-muted)', fontSize: 11 }}> (+{l.borradores} borr.)</span>}</td>
      <td style={{ ...TD, ...TD_NUM }}>{fmtNum(l.enviados)}</td>
      <td style={{ ...TD, ...TD_NUM }} title={l.cola != null ? `Cola aproximada: ${fmtNum(l.cola)}` : 'Sin corte para estimar la cola'}>{fmtPct(l.pctColaEnviada)}</td>
      <td style={{ ...TD, ...TD_NUM }}>{fmtNum(l.buscados)} <span style={{ color: 'var(--z-text-muted)', fontSize: 11 }}>({fmtPct(l.pctRetorno)})</span></td>
      <td style={{ ...TD, ...TD_NUM, color: l.localizados > 0 ? '#16a34a' : undefined }}>{fmtNum(l.localizados)}</td>
      <td style={{ ...TD, ...TD_NUM }}>{l.diasRetornoPromedio != null ? `${l.diasRetornoPromedio.toLocaleString('es-CO')} d` : '—'}</td>
      <td style={{ ...TD, ...TD_NUM }}>{fmtNum(l.gestionados)}</td>
      <td style={{ ...TD, ...TD_NUM }}>{fmtNum(l.pasaronAContacto)}</td>
      <td style={{ ...TD, ...TD_NUM }} title={`${fmtNum(l.titularesConPago)} titulares · honorarios ${cop(l.honorarios)}`}>
        {fmtNum(l.titularesConPago)} <span style={{ color: 'var(--z-text-muted)', fontSize: 11 }}>({cop(l.valorPagado)})</span>
      </td>
    </tr>
  );

  return (
    <div style={{ display: 'flex', flexDirection: 'column', gap: 10 }}>
      {uso.avisos.map(a => (
        <p key={a} style={{ margin: 0, fontSize: 12, color: '#92400e', background: 'rgba(245,158,11,0.10)', border: '1px solid rgba(245,158,11,0.35)', borderRadius: 8, padding: '8px 12px' }}>{a}</p>
      ))}
      <div style={{ overflowX: 'auto', margin: '0 -16px' }}>
        <table style={{ width: '100%', borderCollapse: 'collapse', fontSize: 13 }}>
          <thead>
            <tr>
              <th style={{ ...TH, textAlign: 'left' }}>Líder</th>
              <th style={{ ...TH, textAlign: 'right' }}>Lotes</th>
              <th style={{ ...TH, textAlign: 'right' }} title="Filas enviadas (titular × fuente)">Enviados</th>
              <th style={{ ...TH, textAlign: 'right' }} title="Enviados / cola sugerida aproximada del último corte">% de su cola</th>
              <th style={{ ...TH, textAlign: 'right' }} title="Buscados tras el envío (% de retorno)">Buscados</th>
              <th style={{ ...TH, textAlign: 'right' }}>Localizados</th>
              <th style={{ ...TH, textAlign: 'right' }}>Días al retorno</th>
              <th style={{ ...TH, textAlign: 'right' }}>Gestionados</th>
              <th style={{ ...TH, textAlign: 'right' }} title="Pasaron de perfil sin contacto a con contacto">Contacto</th>
              <th style={{ ...TH, textAlign: 'right' }}>Pagaron después</th>
            </tr>
          </thead>
          <tbody>
            {uso.lideres.map(l => fila(l))}
            {uso.lideres.length > 1 && fila(uso.total, true)}
          </tbody>
        </table>
      </div>
      <p style={{ margin: 0, fontSize: 11, color: 'var(--z-text-muted)', lineHeight: 1.5 }}>
        Lotes enviados o cerrados entre el {fmtFecha(uso.desde)} y el {fmtFecha(uso.hasta)} (por fecha de envío).
        &quot;% de su cola&quot; = filas enviadas / candidatos aproximados del corte{uso.corteCola ? ` del ${fmtFechaHora(uso.corteCola)}` : ''}
        {' '}(falta por buscar + no encontrados viejos en las fuentes Funcionales, antes de exclusiones): es un piso.
        Pagos: cualquier pago del titular posterior al día del envío.
      </p>
    </div>
  );
}
