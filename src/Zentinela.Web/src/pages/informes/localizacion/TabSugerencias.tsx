import { useEffect, useMemo, useRef, useState } from 'react';
import { useMutation, useQuery } from '@tanstack/react-query';
import {
  descargarSugerenciasExcel, obtenerSugerenciasLocalizacion, problemaSugerencias,
} from '../../../api/informesLocalizacionApi';
import { obtenerFuentesLocalizacion } from '../../../api/fuentesLocalizacionApi';
import type {
  CategoriaSugerencia, DesglosePrioridad, FilaSugerencia, ParametrosSugerencias, SugerenciasLocalizacion,
} from '../../../types/informesLocalizacion';
import { COLOR_ESTADO_FUENTE } from '../../../types/fuentesLocalizacion';
import {
  C, KpiCard, TD, TH, buildExportarPDF, cop, filtrarColumnas,
} from '../../../components/informes/reporteCompartido';
import type { DownloadRegistrar, PdfExportDescriptor, PdfTableData } from '../../../components/informes/reporteCompartido';
import {
  ChipEstadoFuente, FiltrosAlcance, Opciones, TD_NUM, Tarjeta, fmtFecha, fmtFechaHora, fmtNum, type Alcance,
} from './compartidos';

// ── Etiquetas y colores ───────────────────────────────────────────────────────

const CATEGORIA: Record<CategoriaSugerencia, { label: string; color: string }> = {
  NUNCA_BUSCADO: { label: 'Nunca buscado',        color: '#2563eb' },
  NO_ENC_VIEJO:  { label: 'No encontrado y viejo', color: '#F59E0B' },
  ENC_VIEJO:     { label: 'Encontrado pero viejo', color: '#16A34A' },
};

const PARTES_DESGLOSE: { k: keyof DesglosePrioridad; label: string }[] = [
  { k: 'perfil',            label: 'Perfil (sin datos / sin contacto)' },
  { k: 'saldo',             label: 'Saldo frente al P90 del par' },
  { k: 'gestion',           label: 'Antigüedad de la última gestión' },
  { k: 'nuncaFuncional',    label: 'Nunca buscado en ninguna Funcional' },
  { k: 'nuncaFuente',       label: 'Nunca buscado en esta fuente' },
  { k: 'noEncontradoViejo', label: 'No encontrado y el dato es viejo' },
  { k: 'contactoFresco',    label: 'Ya tiene contacto fresco' },
];

const colorPrioridad = (p: number) => (p >= 80 ? '#dc2626' : p >= 50 ? '#ea580c' : p >= 20 ? '#ca8a04' : 'var(--z-text-muted)');

// ── Prioridad con su desglose (popover al pasar el mouse o con el foco) ───────

function Prioridad({ fila }: { fila: FilaSugerencia }) {
  const [abierto, setAbierto] = useState(false);
  const color = colorPrioridad(fila.prioridad);
  return (
    <span
      style={{ position: 'relative', display: 'inline-block' }}
      onMouseEnter={() => setAbierto(true)}
      onMouseLeave={() => setAbierto(false)}
    >
      <button
        type="button"
        onFocus={() => setAbierto(true)}
        onBlur={() => setAbierto(false)}
        aria-label={`Prioridad ${fila.prioridad}: ver desglose`}
        style={{
          minWidth: 40, padding: '2px 8px', borderRadius: 6, border: `1px solid ${color}55`,
          background: `${color}18`, color, fontWeight: 700, fontSize: 12, cursor: 'help',
          fontVariantNumeric: 'tabular-nums',
        }}
      >
        {fila.prioridad}
      </button>
      {abierto && (
        <div role="tooltip" style={{
          position: 'absolute', zIndex: 20, top: '100%', left: 0, marginTop: 6, width: 280,
          background: 'var(--z-bg-card)', border: '1px solid var(--z-border)', borderRadius: 8,
          boxShadow: '0 8px 24px rgba(0,0,0,0.14)', padding: '10px 12px', fontSize: 12, whiteSpace: 'normal',
        }}>
          <p style={{ margin: '0 0 6px', fontWeight: 600, color: 'var(--z-text-primary)' }}>
            Por qué tiene {fila.prioridad} puntos
          </p>
          {PARTES_DESGLOSE.map(p => {
            const v = fila.desglose[p.k];
            return (
              <div key={p.k} style={{ display: 'flex', justifyContent: 'space-between', gap: 10, padding: '2px 0',
                color: v === 0 ? 'var(--z-text-muted)' : 'var(--z-text-primary)', opacity: v === 0 ? 0.6 : 1 }}>
                <span>{p.label}</span>
                <strong style={{ color: v < 0 ? '#dc2626' : v > 0 ? '#16a34a' : undefined, fontVariantNumeric: 'tabular-nums' }}>
                  {v > 0 ? `+${v}` : v}
                </strong>
              </div>
            );
          })}
          {fila.encontradoEn.length > 0 && (
            <p style={{ margin: '6px 0 0', color: 'var(--z-text-muted)' }}>Ya encontrado en: {fila.encontradoEn.join(', ')}</p>
          )}
        </div>
      )}
    </span>
  );
}

function Check({ label, valor, onChange, title }: { label: string; valor: boolean; onChange: (v: boolean) => void; title?: string }) {
  return (
    <label title={title} style={{ display: 'flex', alignItems: 'center', gap: 6, fontSize: 12, color: 'var(--z-text-muted)', cursor: 'pointer', whiteSpace: 'nowrap' }}>
      <input type="checkbox" checked={valor} onChange={e => onChange(e.target.checked)} />
      {label}
    </label>
  );
}

function Aviso({ color, children }: { color: 'ambar' | 'rojo' | 'azul'; children: React.ReactNode }) {
  const c = { ambar: ['#92400e', 'rgba(245,158,11,0.10)', 'rgba(245,158,11,0.35)'],
              rojo:  ['#991b1b', 'rgba(220,38,38,0.08)', 'rgba(220,38,38,0.30)'],
              azul:  ['#1e40af', 'rgba(37,99,235,0.08)', 'rgba(37,99,235,0.30)'] }[color];
  return (
    <div style={{ fontSize: 12, color: c[0], background: c[1], border: `1px solid ${c[2]}`, borderRadius: 8, padding: '10px 14px', lineHeight: 1.5 }}>
      {children}
    </div>
  );
}

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

// ╔══════════════════════════════════════════════════════════════════════════════╗
// ║  TAB — SUGERENCIAS DE LOCALIZACIÓN (etapa 2)                                  ║
// ║  Consulta manual (botón), en línea sobre el par: no depende del precálculo.   ║
// ╚══════════════════════════════════════════════════════════════════════════════╝
export function TabSugerencias({ alcance, onAlcance, onRegisterDownload }: {
  alcance: Alcance; onAlcance: (a: Alcance) => void; onRegisterDownload?: DownloadRegistrar;
}) {
  const { data: catalogo = [] } = useQuery({
    queryKey: ['fuentes-localizacion'],
    queryFn: obtenerFuentesLocalizacion,
    staleTime: 10 * 60 * 1000,
  });
  const funcionales = useMemo(() => catalogo.filter(f => f.enPanelCalificador && f.estado === 'Funcional'), [catalogo]);
  const noFuncionales = useMemo(() => catalogo.filter(f => f.enPanelCalificador && f.estado !== 'Funcional'), [catalogo]);

  // null = todas las Funcionales (lo predeterminado).
  const [elegidas, setElegidas] = useState<string[] | null>(null);
  const [umbralDias, setUmbralDias] = useState(180);
  const [refresco, setRefresco] = useState(0);
  const [soloConSaldo, setSoloConSaldo] = useState(true);
  const [incluirLocalizados, setIncluirLocalizados] = useState(false);
  const [top, setTop] = useState(500);
  const [excluirEnviados, setExcluirEnviados] = useState(60);
  const [descargando, setDescargando] = useState(false);
  const [errorExcel, setErrorExcel] = useState<string | null>(null);

  const seleccion = elegidas ?? funcionales.map(f => f.clave);
  const parametros: ParametrosSugerencias = {
    liderID: alcance.liderID || null,
    carteraID: alcance.carteraID,
    fuentes: elegidas === null ? [] : elegidas,
    umbralDias,
    umbralRefrescoDias: refresco || null,
    soloConSaldo,
    top,
    excluirEnviadosDias: excluirEnviados,
    incluirLocalizados,
  };
  const claveParametros = JSON.stringify(parametros);

  const consulta = useMutation({
    mutationFn: async (p: ParametrosSugerencias) => ({ clave: JSON.stringify(p), p, datos: await obtenerSugerenciasLocalizacion(p) }),
  });
  const [problema, setProblema] = useState<{ etl: boolean; mensaje: string } | null>(null);
  useEffect(() => {
    if (!consulta.isError) { setProblema(null); return; }
    let vivo = true;
    void problemaSugerencias(consulta.error).then(p => { if (vivo) setProblema(p); });
    return () => { vivo = false; };
  }, [consulta.isError, consulta.error]);

  const resultado = consulta.data;
  const datos: SugerenciasLocalizacion | undefined = resultado?.datos;
  const desactualizado = !!resultado && resultado.clave !== claveParametros;

  const latestRef = useRef<SugerenciasLocalizacion | undefined>(undefined);
  useEffect(() => { latestRef.current = datos; }, [datos]);

  // PDF del top (el Excel del cargue es aparte, con su botón).
  useEffect(() => {
    if (!onRegisterDownload) return;
    onRegisterDownload((): PdfExportDescriptor | null => {
      const d = latestRef.current;
      if (!d || d.filas.length === 0) return null;
      const head = ['Prioridad', 'Titular', 'Nombre', 'Fuente', 'Categoría', 'Última consulta', 'Saldo', 'Último perfil', 'Última gestión', 'Ya encontrado en'];
      const body = d.filas.map(f => [
        String(f.prioridad), String(f.titularID), f.nombre ?? '', f.etiquetaFuente, CATEGORIA[f.categoria].label,
        fmtFecha(f.ultimaConsulta), f.saldo != null ? cop(f.saldo) : '—', f.ultimoPerfil ?? '—', fmtFecha(f.fechaUltimaGestion),
        f.encontradoEn.join(', '),
      ]);
      const pdfTable: PdfTableData = {
        head: [head], body, cumpColIndex: -1,
        colStyles: { 0: { halign: 'center' }, 2: { halign: 'left' }, 3: { halign: 'left' }, 4: { halign: 'left' }, 7: { halign: 'left' }, 9: { halign: 'left' } },
      };
      return {
        columnas: head,
        generar: idx => buildExportarPDF({
          tituloArchivo: `Localizacion_Sugerencias_${d.etiquetaAlcance.replace(/\W+/g, '_')}`,
          tituloPDF: `Sugerencias de localización — ${d.etiquetaAlcance}`,
          subtitulo: `Top ${fmtNum(d.filas.length)} de ${fmtNum(d.kpis.candidatos)} candidatos · gestión al ${fmtFechaHora(d.datosGestionAl)}`,
          pdfTable: filtrarColumnas(pdfTable, idx),
        }),
      };
    });
    return () => onRegisterDownload(null);
  }, [onRegisterDownload]);

  const alternar = (clave: string) => {
    const base = elegidas ?? funcionales.map(f => f.clave);
    const nueva = base.includes(clave) ? base.filter(c => c !== clave) : [...base, clave];
    setElegidas(nueva.length === funcionales.length ? null : nueva);
  };

  const puedeGenerar = alcance.carteraID > 0 && seleccion.length > 0 && !consulta.isPending;

  const descargarExcel = async () => {
    if (!resultado || descargando) return;
    setDescargando(true);
    setErrorExcel(null);
    try {
      const { blob, extension } = await descargarSugerenciasExcel(resultado.p);
      const nombre = `Localizacion_${resultado.datos.etiquetaAlcance.replace(/\W+/g, '_')}_${new Date().toISOString().slice(0, 10)}.${extension}`;
      guardarBlob(blob, nombre);
    } catch (e) {
      setErrorExcel((await problemaSugerencias(e)).mensaje);
    } finally {
      setDescargando(false);
    }
  };

  const etiqueta: React.CSSProperties = { fontSize: 12, fontWeight: 600, color: 'var(--z-text-muted)', whiteSpace: 'nowrap' };

  return (
    <div style={{ display: 'flex', flexDirection: 'column', gap: 20 }}>

      {/* Filtros */}
      <Tarjeta titulo="¿A quién mandar a localizar?">
        <div style={{ display: 'flex', flexDirection: 'column', gap: 14 }}>
          <FiltrosAlcance alcance={alcance} onChange={onAlcance} />
          {alcance.carteraID === 0 && (
            <p style={{ margin: 0, fontSize: 12, color: 'var(--z-text-muted)' }}>
              Elige una cartera. {alcance.liderID === 0 ? 'Sin líder se sugiere sobre la cartera completa (todos sus líderes activos).' : ''}
            </p>
          )}

          <div style={{ display: 'flex', flexDirection: 'column', gap: 6 }}>
            <div style={{ display: 'flex', alignItems: 'center', gap: 10, flexWrap: 'wrap' }}>
              <span style={etiqueta}>Fuentes (solo Funcionales):</span>
              <button type="button" onClick={() => setElegidas(null)} style={{
                fontSize: 11, border: 'none', background: 'none', color: 'var(--z-brand)', cursor: 'pointer', padding: 0,
              }}>todas</button>
              <button type="button" onClick={() => setElegidas([])} style={{
                fontSize: 11, border: 'none', background: 'none', color: 'var(--z-brand)', cursor: 'pointer', padding: 0,
              }}>ninguna</button>
            </div>
            <div style={{ display: 'flex', gap: 6, flexWrap: 'wrap' }}>
              {funcionales.map(f => {
                const activa = seleccion.includes(f.clave);
                const color = COLOR_ESTADO_FUENTE[f.estado];
                return (
                  <button key={f.clave} type="button" onClick={() => alternar(f.clave)} aria-pressed={activa}
                    title={f.ultimoCargueOk ? `Último cargue OK: ${fmtFecha(f.ultimoCargueOk)}` : 'Sin cargues por el Cargue de Fuentes'}
                    style={{
                      display: 'flex', alignItems: 'center', gap: 6, padding: '4px 10px', borderRadius: 999, fontSize: 12,
                      border: `1px solid ${activa ? color : 'var(--z-border)'}`,
                      background: activa ? `${color}18` : 'transparent',
                      color: activa ? 'var(--z-text-primary)' : 'var(--z-text-muted)', cursor: 'pointer',
                    }}>
                    <span style={{ width: 8, height: 8, borderRadius: '50%', background: activa ? color : 'var(--z-border)' }} />
                    {f.etiquetaCalificador}
                  </button>
                );
              })}
            </div>
            {noFuncionales.length > 0 && (
              <div style={{ display: 'flex', gap: 6, flexWrap: 'wrap', alignItems: 'center', fontSize: 11, color: 'var(--z-text-muted)' }}>
                <span>No se sugieren:</span>
                {noFuncionales.map(f => (
                  <span key={f.clave} style={{ display: 'flex', alignItems: 'center', gap: 4, opacity: 0.8 }}>
                    {f.etiquetaCalificador} <ChipEstadoFuente estado={f.estado} nota={f.nota} />
                  </span>
                ))}
              </div>
            )}
          </div>

          <div style={{ display: 'flex', gap: 18, flexWrap: 'wrap', alignItems: 'center' }}>
            <Opciones<number>
              label="No encontrado y viejo: más de"
              opciones={[{ value: 90, label: '90 d' }, { value: 180, label: '180 d' }, { value: 365, label: '365 d' }]}
              valor={umbralDias} onChange={setUmbralDias}
            />
            <Opciones<number>
              label="Refrescar encontrados de más de"
              opciones={[{ value: 0, label: 'No' }, { value: 180, label: '180 d' }, { value: 365, label: '365 d' }, { value: 730, label: '2 años' }]}
              valor={refresco} onChange={setRefresco}
            />
          </div>
          <div style={{ display: 'flex', gap: 18, flexWrap: 'wrap', alignItems: 'center' }}>
            <Opciones<number>
              label="Top"
              opciones={[100, 500, 1000, 2000, 5000].map(v => ({ value: v, label: fmtNum(v) }))}
              valor={top} onChange={setTop}
            />
            <Opciones<number>
              label="Excluir enviados en los últimos"
              opciones={[{ value: 0, label: 'No' }, { value: 30, label: '30 d' }, { value: 60, label: '60 d' }, { value: 90, label: '90 d' }]}
              valor={excluirEnviados} onChange={setExcluirEnviados}
            />
            <Check label="Solo con saldo" valor={soloConSaldo} onChange={setSoloConSaldo} />
            <Check label="Incluir localizados" valor={incluirLocalizados} onChange={setIncluirLocalizados}
              title="Sin marcar, a quien ya tiene celular o email de hace 180 días o menos en una fuente Funcional se le restan 100 puntos" />
          </div>

          <div style={{ display: 'flex', gap: 10, alignItems: 'center', flexWrap: 'wrap' }}>
            <button
              type="button"
              onClick={() => consulta.mutate(parametros)}
              disabled={!puedeGenerar}
              style={{
                padding: '8px 18px', borderRadius: 7, fontSize: 13, fontWeight: 600, border: 'none',
                background: puedeGenerar ? C.brand : '#94a3b8', color: '#fff', cursor: puedeGenerar ? 'pointer' : 'not-allowed',
              }}
            >
              {consulta.isPending ? 'Calculando…' : 'Generar sugerencias'}
            </button>
            {consulta.isPending && (
              <span style={{ fontSize: 12, color: 'var(--z-text-muted)' }}>Se calcula en línea sobre los titulares del par; puede tardar unos segundos.</span>
            )}
            {desactualizado && !consulta.isPending && (
              <span style={{ fontSize: 12, color: '#ca8a04' }}>Cambiaste los filtros: vuelve a generar para actualizar.</span>
            )}
          </div>
        </div>
      </Tarjeta>

      {/* Errores */}
      {consulta.isError && problema && (
        <Aviso color={problema.etl ? 'azul' : 'rojo'}>
          {problema.etl && <strong>Los datos de gestión se están actualizando. </strong>}
          {problema.mensaje}
        </Aviso>
      )}

      {datos && <Resultados datos={datos} descargando={descargando} errorExcel={errorExcel} onExcel={descargarExcel} />}
    </div>
  );
}

// ── Resultados ────────────────────────────────────────────────────────────────

function Resultados({ datos, descargando, errorExcel, onExcel }: {
  datos: SugerenciasLocalizacion; descargando: boolean; errorExcel: string | null; onExcel: () => void;
}) {
  const k = datos.kpis;
  const refresco = datos.parametros.umbralRefrescoDias != null;
  const excluidos = k.candidatosAntes - k.candidatos;

  return (
    <div style={{ display: 'flex', flexDirection: 'column', gap: 20 }}>
      <div style={{
        display: 'flex', alignItems: 'center', gap: 10, flexWrap: 'wrap', fontSize: 12, color: 'var(--z-text-muted)',
        background: 'var(--z-bg-card)', border: '1px solid var(--z-border)', borderRadius: 8, padding: '8px 14px',
      }}>
        <span><strong style={{ color: 'var(--z-text-primary)' }}>{datos.etiquetaAlcance}</strong> · {fmtNum(k.titularesPar)} titulares</span>
        <span>· Gestión al <strong style={{ color: 'var(--z-text-primary)' }}>{fmtFechaHora(datos.datosGestionAl)}</strong></span>
        <span style={{ opacity: 0.8 }}>· calculado en {(datos.duracionMs / 1000).toLocaleString('es-CO', { maximumFractionDigits: 1 })} s</span>
      </div>

      {datos.avisos.map(a => <Aviso key={a} color="ambar">{a}</Aviso>)}

      <div style={{ display: 'flex', gap: 12, flexWrap: 'wrap' }}>
        <KpiCard label="Candidatos (titular × fuente)" value={fmtNum(k.candidatos)}
          sub={excluidos > 0 ? `${fmtNum(k.candidatosAntes)} antes de exclusiones` : undefined} color={C.brand} />
        <KpiCard label="Titulares a buscar" value={fmtNum(k.titularesCandidatos)} sub={`de ${fmtNum(k.titularesPar)} del par`} />
        <KpiCard label="Saldo de esos titulares" value={cop(k.saldoCandidatos)}
          sub={k.p90Saldo != null ? `P90 del par: ${cop(k.p90Saldo)}` : 'sin saldo en el par'} />
        <KpiCard label="Con contacto fresco" value={fmtNum(k.conContactoFresco)}
          sub={datos.parametros.incluirLocalizados ? 'sin penalizar (incluir localizados)' : '−100 puntos: van al final'}
          color={k.conContactoFresco > 0 ? '#ca8a04' : undefined} />
      </div>

      <div style={{ display: 'grid', gridTemplateColumns: 'repeat(auto-fit, minmax(320px, 1fr))', gap: 16 }}>
        <Tarjeta titulo="Por categoría">
          <div style={{ display: 'flex', flexDirection: 'column', gap: 8 }}>
            {k.porCategoria.map(c => (
              <div key={c.categoria} style={{ display: 'flex', justifyContent: 'space-between', alignItems: 'center', fontSize: 13 }}>
                <span style={{ display: 'flex', alignItems: 'center', gap: 8 }}>
                  <span style={{ width: 10, height: 10, borderRadius: 3, background: CATEGORIA[c.categoria].color }} />
                  {c.etiqueta}
                </span>
                <span style={{ fontVariantNumeric: 'tabular-nums' }}>
                  <strong>{fmtNum(c.candidatos)}</strong>
                  <span style={{ color: 'var(--z-text-muted)', fontSize: 12 }}> de {fmtNum(c.antes)}</span>
                </span>
              </div>
            ))}
          </div>
        </Tarjeta>
        <Tarjeta titulo="Exclusiones">
          {k.exclusiones.length === 0 ? (
            <p style={{ margin: 0, fontSize: 12, color: 'var(--z-text-muted)' }}>Nadie quedó excluido.</p>
          ) : (
            <div style={{ display: 'flex', flexDirection: 'column', gap: 8 }}>
              {k.exclusiones.map(e => (
                <div key={e.motivo} style={{ display: 'flex', justifyContent: 'space-between', fontSize: 13 }}>
                  <span>{e.etiqueta}</span>
                  <span style={{ fontVariantNumeric: 'tabular-nums' }}>
                    <strong>{fmtNum(e.titulares)}</strong>
                    <span style={{ color: 'var(--z-text-muted)', fontSize: 12 }}> titulares · {fmtNum(e.filas)} filas</span>
                  </span>
                </div>
              ))}
            </div>
          )}
        </Tarjeta>
      </div>

      {/* Por fuente */}
      <div style={{ background: 'var(--z-bg-card)', border: '1px solid var(--z-border)', borderRadius: 10, overflow: 'hidden' }}>
        <div style={{ overflowX: 'auto' }}>
          <table style={{ width: '100%', borderCollapse: 'collapse', fontSize: 13 }}>
            <thead>
              <tr>
                <th style={{ ...TH, textAlign: 'left' }}>Fuente</th>
                <th style={{ ...TH, textAlign: 'right' }} title="Antes de exclusiones = Falta por buscar del Calificador">Nunca buscado</th>
                <th style={{ ...TH, textAlign: 'right' }}>No enc. y viejo</th>
                {refresco && <th style={{ ...TH, textAlign: 'right' }}>Enc. pero viejo</th>}
                <th style={{ ...TH, textAlign: 'right' }}>Excluidos</th>
                <th style={{ ...TH, textAlign: 'right' }}>Candidatos</th>
                <th style={{ ...TH, textAlign: 'right' }}>Con contacto fresco</th>
                <th style={{ ...TH, textAlign: 'right' }}>Saldo</th>
              </tr>
            </thead>
            <tbody>
              {k.porFuente.map(f => (
                <tr key={f.fuente}>
                  <td style={{ ...TD, fontWeight: 500, color: 'var(--z-text-primary)' }}>{f.etiqueta}</td>
                  <td style={{ ...TD, ...TD_NUM }} title={`${fmtNum(f.nuncaAntes)} antes de exclusiones`}>
                    {fmtNum(f.nunca)} <span style={{ color: 'var(--z-text-muted)', fontSize: 11 }}>/ {fmtNum(f.nuncaAntes)}</span>
                  </td>
                  <td style={{ ...TD, ...TD_NUM }}>
                    {fmtNum(f.noEncontradoViejo)} <span style={{ color: 'var(--z-text-muted)', fontSize: 11 }}>/ {fmtNum(f.noEncontradoViejoAntes)}</span>
                  </td>
                  {refresco && (
                    <td style={{ ...TD, ...TD_NUM }}>
                      {fmtNum(f.encontradoViejo)} <span style={{ color: 'var(--z-text-muted)', fontSize: 11 }}>/ {fmtNum(f.encontradoViejoAntes)}</span>
                    </td>
                  )}
                  <td style={{ ...TD, ...TD_NUM, color: 'var(--z-text-muted)' }}>{fmtNum(f.excluidos)}</td>
                  <td style={{ ...TD, ...TD_NUM, fontWeight: 600 }}>{fmtNum(f.candidatos)}</td>
                  <td style={{ ...TD, ...TD_NUM }}>{fmtNum(f.conContactoFresco)}</td>
                  <td style={{ ...TD, ...TD_NUM }}>{cop(f.saldoCandidatos)}</td>
                </tr>
              ))}
            </tbody>
          </table>
        </div>
      </div>

      {/* Top */}
      <Tarjeta
        titulo={`Top ${fmtNum(datos.filas.length)} por prioridad${datos.hayMas ? ` (de ${fmtNum(k.candidatos)})` : ''}`}
        derecha={
          <div style={{ display: 'flex', alignItems: 'center', gap: 10 }}>
            {errorExcel && <span style={{ fontSize: 12, color: '#dc2626' }}>{errorExcel}</span>}
            <button
              type="button"
              onClick={onExcel}
              disabled={descargando || datos.filas.length === 0}
              title="Formato del Cargue de Fuentes: un .xlsx por fuente (un .zip si son varias), listo para que lo llene el proveedor y se cargue tal cual"
              style={{
                padding: '6px 14px', borderRadius: 7, fontSize: 13, fontWeight: 500, border: 'none',
                background: descargando || datos.filas.length === 0 ? '#94a3b8' : '#0f766e', color: '#fff',
                cursor: descargando || datos.filas.length === 0 ? 'not-allowed' : 'pointer', whiteSpace: 'nowrap',
              }}
            >
              {descargando ? 'Generando…' : '↓ Excel para el cargue'}
            </button>
          </div>
        }
      >
        {datos.filas.length === 0 ? (
          <p style={{ margin: 0, textAlign: 'center', padding: '24px 0', fontSize: 13, color: 'var(--z-text-muted)' }}>
            No quedó ningún candidato con estos filtros.
          </p>
        ) : (
          <div style={{ overflowX: 'auto', margin: '0 -16px' }}>
            <table style={{ width: '100%', borderCollapse: 'collapse', fontSize: 13 }}>
              <thead>
                <tr>
                  <th style={{ ...TH, textAlign: 'center' }}>Prioridad</th>
                  <th style={{ ...TH, textAlign: 'left' }}>Titular</th>
                  <th style={{ ...TH, textAlign: 'left' }}>Nombre</th>
                  <th style={{ ...TH, textAlign: 'left' }}>Fuente</th>
                  <th style={{ ...TH, textAlign: 'left' }}>Categoría</th>
                  <th style={{ ...TH, textAlign: 'center' }}>Última consulta</th>
                  <th style={{ ...TH, textAlign: 'right' }}>Saldo</th>
                  <th style={{ ...TH, textAlign: 'left' }}>Último perfil</th>
                  <th style={{ ...TH, textAlign: 'center' }}>Última gestión</th>
                  <th style={{ ...TH, textAlign: 'left' }}>Ya encontrado en</th>
                </tr>
              </thead>
              <tbody>
                {datos.filas.map(f => (
                  <tr key={`${f.titularID}|${f.fuente}`}
                    onMouseEnter={e => { e.currentTarget.style.background = 'var(--z-bg-page)'; }}
                    onMouseLeave={e => { e.currentTarget.style.background = 'transparent'; }}>
                    <td style={{ ...TD, textAlign: 'center' }}><Prioridad fila={f} /></td>
                    <td style={{ ...TD, fontVariantNumeric: 'tabular-nums' }}>{f.titularID}</td>
                    <td style={{ ...TD, color: 'var(--z-text-primary)', maxWidth: 220, overflow: 'hidden', textOverflow: 'ellipsis' }} title={f.nombre ?? undefined}>
                      {f.nombre ?? '—'}
                    </td>
                    <td style={TD}>{f.etiquetaFuente}</td>
                    <td style={TD}>
                      <span style={{ color: CATEGORIA[f.categoria].color, fontWeight: 600, fontSize: 12 }}>{CATEGORIA[f.categoria].label}</span>
                    </td>
                    <td style={{ ...TD, textAlign: 'center', color: 'var(--z-text-muted)' }}>{f.ultimaConsulta ? fmtFecha(f.ultimaConsulta) : 'nunca'}</td>
                    <td style={{ ...TD, ...TD_NUM }}>{f.saldo != null ? cop(f.saldo) : '—'}</td>
                    <td style={{ ...TD, color: 'var(--z-text-muted)' }}>{f.ultimoPerfil ?? '—'}</td>
                    <td style={{ ...TD, textAlign: 'center', color: 'var(--z-text-muted)' }}>{f.fechaUltimaGestion ? fmtFecha(f.fechaUltimaGestion) : 'nunca'}</td>
                    <td style={{ ...TD, color: 'var(--z-text-muted)', fontSize: 12 }}>{f.encontradoEn.length ? f.encontradoEn.join(', ') : '—'}</td>
                  </tr>
                ))}
              </tbody>
            </table>
          </div>
        )}
      </Tarjeta>

      <p style={{ margin: 0, fontSize: 11, color: 'var(--z-text-muted)', lineHeight: 1.5 }}>
        Puntaje: +40 perfil ilocalizado, equivocado o de datos de localización · +25 otro perfil sin contacto o sin gestión ·
        hasta +30 por saldo frente al P90 del par · +15 sin gestión en 90 días (+5 en 30) · +15 nunca buscado en ninguna
        fuente Funcional · +10 nunca buscado en la fuente · +5 no encontrado y viejo · −100 si ya tiene contacto fresco.
        Se excluyen paz y salvo, fallecidos, no gestionar, PQR, acuerdos, promesas vigentes y los ya enviados a esa fuente.
      </p>
    </div>
  );
}
