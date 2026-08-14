import { useRef, useState } from 'react';
import { useMutation } from '@tanstack/react-query';
import { ZButton } from '../../../components/ZButton';
import { CargandoSpiral } from '../../../components/CargandoSpiral';
import { C, KpiCard, TD, ThSort, useSortable } from '../../../components/informes/reporteCompartido';
import { analizarUsoBaseDatos } from '../../../api/informesGeneralesApi';
import type { UsoBaseDatos as UsoBaseDatosDto, UsoPorFuente } from '../../../types/informesGenerales';
import { PanelColumnasAceptadas } from './PanelColumnasAceptadas';
import { TablaPorLider } from './TablaPorLider';

type Etapa = 'inicial' | 'resultado';

const MAX_ARCHIVOS = 20;
const MAX_BYTES    = 120 * 1024 * 1024;
/** Tope en MB para los textos de la UI — derivado de MAX_BYTES para que no se desincronicen. */
const MAX_MB       = MAX_BYTES / 1024 / 1024;

const num = (v: number) => v.toLocaleString('es-CO');

/** Identidad estable de un archivo, para deduplicar al acumular selecciones. */
const claveArchivo = (f: File) => `${f.name}|${f.size}|${f.lastModified}`;

/**
 * Traduce un error de axios a un mensaje accionable.
 *
 * El backend responde `{ mensaje }` en sus errores controlados, pero los fallos que
 * ocurren ANTES de llegar al controller (límite de tamaño de IIS/Kestrel, fallo de
 * binding del multipart, excepción al activar el controller) devuelven otro formato
 * o ningún cuerpo. Un fallback genérico ahí deja al usuario —y a quien depure— sin
 * ninguna pista, así que se conserva siempre el código de estado.
 */
function describirError(err: unknown): string {
  const e = err as {
    response?: { status?: number; data?: unknown };
    code?: string;
    message?: string;
  };

  // Sin respuesta: la petición nunca llegó (API caída, CORS, red).
  if (!e?.response) {
    return `No se pudo contactar al servidor (${e?.code ?? e?.message ?? 'sin respuesta'}). ` +
           'Verifica que la API esté arriba e inténtalo de nuevo.';
  }

  const status = e.response.status ?? 0;
  const data   = e.response.data;

  // Error controlado del backend.
  if (data && typeof data === 'object' && 'mensaje' in data) {
    const m = (data as { mensaje?: unknown }).mensaje;
    if (typeof m === 'string' && m.length > 0) return m;
  }

  // ValidationProblemDetails / ProblemDetails de ASP.NET.
  if (data && typeof data === 'object') {
    const pd = data as { title?: unknown; detail?: unknown; errors?: Record<string, unknown> };
    if (pd.errors && typeof pd.errors === 'object') {
      const detalle = Object.entries(pd.errors)
        .map(([campo, msgs]) => `${campo}: ${Array.isArray(msgs) ? msgs.join(' ') : String(msgs)}`)
        .join(' · ');
      if (detalle) return `Error ${status} — ${detalle}`;
    }
    if (typeof pd.detail === 'string' && pd.detail) return `Error ${status} — ${pd.detail}`;
    if (typeof pd.title  === 'string' && pd.title)  return `Error ${status} — ${pd.title}`;
  }

  if (typeof data === 'string' && data.trim().length > 0 && !data.trimStart().startsWith('<')) {
    return `Error ${status} — ${data.slice(0, 300)}`;
  }

  // Casos sin cuerpo útil: se nombra la causa probable en vez de un genérico.
  if (status === 413) return 'Error 413 — los archivos superan el tamaño máximo que acepta el servidor.';
  if (status === 403) return 'Error 403 — tu usuario no tiene permiso (se requiere Admin o Director).';
  if (status === 404) return 'Error 404 — el endpoint no existe en la API. Es probable que falte re-publicar el backend.';
  if (status >= 500)  return `Error ${status} — falla interna del servidor. Revisa el log de la API.`;

  return `Error ${status} — el servidor rechazó la carga sin dar un motivo.`;
}

export function UsoBaseDatos() {
  const inputRef = useRef<HTMLInputElement>(null);

  const [etapa, setEtapa]           = useState<Etapa>('inicial');
  const [archivos, setArchivos]     = useState<File[]>([]);
  const [resultado, setResultado]   = useState<UsoBaseDatosDto | null>(null);
  const [errorMensaje, setError]    = useState('');
  const [verCompartidos, setVerCompartidos] = useState(false);
  const [verNoEncontrados, setVerNoEncontrados] = useState(false);

  const bytesTotales = archivos.reduce((s, a) => s + a.size, 0);

  const mutAnalizar = useMutation({
    mutationFn: () => analizarUsoBaseDatos(archivos),
    onSuccess: (data) => {
      setResultado(data);
      setError('');
      setEtapa('resultado');
    },
    onError: (err: unknown) => setError(describirError(err)),
  });

  // Acumula en vez de reemplazar: el usuario puede ir agregando archivos de a tandas.
  const handleSeleccionar = (e: React.ChangeEvent<HTMLInputElement>) => {
    const nuevos = Array.from(e.target.files ?? []);
    if (nuevos.length === 0) return;

    setArchivos((previos) => {
      const vistos = new Set(previos.map(claveArchivo));
      const suma = [...previos, ...nuevos.filter((f) => !vistos.has(claveArchivo(f)))];
      return suma.slice(0, MAX_ARCHIVOS);
    });
    setError('');
    // Permite volver a elegir el mismo archivo después de quitarlo.
    if (inputRef.current) inputRef.current.value = '';
  };

  const quitar = (clave: string) =>
    setArchivos((previos) => previos.filter((f) => claveArchivo(f) !== clave));

  const reiniciar = () => {
    setArchivos([]);
    setResultado(null);
    setError('');
    setEtapa('inicial');
    if (inputRef.current) inputRef.current.value = '';
  };

  const excedeTamano = bytesTotales > MAX_BYTES;
  const puedeAnalizar = archivos.length > 0 && !excedeTamano;

  return (
    <div style={{ display: 'flex', flexDirection: 'column', gap: 24 }}>

      {/* El cruce escanea ~12,2M registros; con cargas de cientos de miles de números
          puede tardar un par de minutos. Se avisa para que no parezca colgado. */}
      {mutAnalizar.isPending && (
        <CargandoSpiral mensaje="Cruzando los números contra las fuentes de localización… con archivos grandes puede tardar un par de minutos." />
      )}

      {/* ── Etapa inicial ─────────────────────────────────────────────────── */}
      {!mutAnalizar.isPending && etapa === 'inicial' && (
        <>
          <PanelColumnasAceptadas />

          <div>
            <div
              onClick={() => inputRef.current?.click()}
              style={{
                border: `1.5px dashed ${archivos.length ? 'var(--z-brand)' : 'var(--z-border)'}`,
                borderRadius: 8, padding: 24, textAlign: 'center', cursor: 'pointer',
                background: archivos.length ? '#eff6ff' : 'transparent',
                transition: 'border-color 0.15s, background 0.15s',
              }}
            >
              <p style={{
                fontSize: 13, marginBottom: 4,
                color: archivos.length ? 'var(--z-brand)' : 'var(--z-text-muted)',
              }}>
                {archivos.length
                  ? `${archivos.length} archivo(s) seleccionado(s) — haz clic para agregar más`
                  : 'Haz clic para seleccionar uno o varios archivos .csv o .xlsx'}
              </p>
              <p style={{ fontSize: 11, color: 'var(--z-text-muted)' }}>
                CSV o XLSX — hasta {MAX_ARCHIVOS} archivos, {MAX_MB} MB en total
                {archivos.length > 0 && ` · ${(bytesTotales / 1024 / 1024).toFixed(2)} MB`}
              </p>
            </div>
            <input
              ref={inputRef}
              type="file"
              accept=".csv,.xlsx"
              multiple
              onChange={handleSeleccionar}
              style={{ display: 'none' }}
            />
          </div>

          {archivos.length > 0 && (
            <div style={{ display: 'flex', flexDirection: 'column', gap: 6 }}>
              {archivos.map((f) => (
                <div
                  key={claveArchivo(f)}
                  style={{
                    display: 'flex', alignItems: 'center', justifyContent: 'space-between',
                    gap: 12, padding: '8px 12px', fontSize: 13,
                    border: '1px solid var(--z-border)', borderRadius: 6,
                  }}
                >
                  <span style={{ overflow: 'hidden', textOverflow: 'ellipsis', whiteSpace: 'nowrap' }}>
                    {f.name}
                  </span>
                  <span style={{ display: 'flex', alignItems: 'center', gap: 12, flexShrink: 0 }}>
                    <span style={{ fontSize: 11, color: 'var(--z-text-muted)' }}>
                      {(f.size / 1024).toFixed(1)} KB
                    </span>
                    <button
                      onClick={() => quitar(claveArchivo(f))}
                      title="Quitar"
                      style={{
                        background: 'none', border: 'none', cursor: 'pointer',
                        color: 'var(--z-text-muted)', fontSize: 15, lineHeight: 1,
                      }}
                    >
                      ×
                    </button>
                  </span>
                </div>
              ))}
              <button
                onClick={reiniciar}
                style={{
                  alignSelf: 'flex-start', background: 'none', border: 'none',
                  cursor: 'pointer', fontSize: 12, color: 'var(--z-text-muted)', padding: 0,
                }}
              >
                Quitar todos
              </button>
            </div>
          )}

          {excedeTamano && (
            <p style={{ fontSize: 13, color: C.rojo, margin: 0 }}>
              Los archivos suman {(bytesTotales / 1024 / 1024).toFixed(2)} MB y el máximo es {MAX_MB} MB.
              Quita alguno o divídelos en varias cargas.
            </p>
          )}

          {errorMensaje && (
            <p style={{ fontSize: 13, color: C.rojo, margin: 0 }}>{errorMensaje}</p>
          )}

          <div>
            <ZButton onClick={() => mutAnalizar.mutate()} disabled={!puedeAnalizar}
              style={puedeAnalizar ? undefined : { opacity: 0.5, cursor: 'not-allowed' }}>
              Analizar
            </ZButton>
          </div>
        </>
      )}

      {/* ── Etapa resultado ───────────────────────────────────────────────── */}
      {!mutAnalizar.isPending && etapa === 'resultado' && resultado && (
        <>
          <div style={{ display: 'flex', gap: 12, flexWrap: 'wrap' }}>
            <KpiCard label="Números únicos cargados" value={num(resultado.telefonosDistintos)}
              sub={`${num(resultado.telefonosCelulares)} celulares · ${num(resultado.telefonosFijos)} fijos`} />
            <KpiCard label="Encontrados en base de datos" value={num(resultado.numerosEnBaseDatos)}
              valueSuffix={`${resultado.porcentajeUso.toFixed(2)}%`} color={C.verde} />
            <KpiCard label="No encontrados" value={num(resultado.numerosNoEncontrados)} />
            <KpiCard label="Titulares distintos" value={num(resultado.titularesDistintos)} />
          </div>

          <div style={{ display: 'flex', gap: 12, flexWrap: 'wrap' }}>
            <KpiCard label="Con líder" value={num(resultado.numerosConLider)} />
            <KpiCard label="Sin líder asignado" value={num(resultado.numerosSinLider)}
              color={resultado.numerosSinLider > 0 ? C.naranja : undefined} />
            <KpiCard label="Compartidos (> 1 líder)" value={num(resultado.numerosCompartidos)} />
            <KpiCard label="Líderes distintos" value={num(resultado.lideresDistintos)}
              sub={`${resultado.duracionMs.toLocaleString('es-CO')} ms`} />
          </div>

          {/* La suma por líder supera el total: se explica en vez de dejar al usuario
              descubrir el descuadre por su cuenta. */}
          {resultado.numerosCompartidos > 0 && (
            <div style={{
              background: '#eff6ff', border: '1px solid #bfdbfe', borderRadius: 8,
              padding: '12px 16px', fontSize: 13, color: '#1e40af', lineHeight: 1.5,
            }}>
              El total cuenta <strong>{num(resultado.numerosEnBaseDatos)}</strong> números distintos.
              La suma del desglose por líder es <strong>{num(resultado.sumaAcreditaciones)}</strong> porque{' '}
              <strong>{num(resultado.numerosCompartidos)}</strong> número(s) pertenecen a titulares de más
              de un líder y se acreditan íntegros a cada uno. La diferencia es acreditación compartida,
              no números adicionales.
            </div>
          )}

          {resultado.advertencias.map((a, i) => (
            <div key={i} style={{
              background: '#fffbeb', border: '1px solid #fde68a', borderRadius: 8,
              padding: '10px 14px', fontSize: 13, color: '#92400e',
            }}>
              {a}
            </div>
          ))}

          <TablaArchivos resultado={resultado} />

          <TablaPorFuente filas={resultado.porFuente} />

          <TablaPorLider
            porLider={resultado.porLider}
            porLiderCartera={resultado.porLiderCartera}
            numerosEnBaseDatos={resultado.numerosEnBaseDatos}
          />

          {resultado.topCompartidos.length > 0 && (
            <Colapsable
              titulo={`Top ${resultado.topCompartidos.length} números compartidos`}
              abierto={verCompartidos}
              onToggle={() => setVerCompartidos((v) => !v)}
            >
              <table style={{ width: '100%', borderCollapse: 'collapse', fontSize: 13 }}>
                <tbody>
                  {resultado.topCompartidos.map((c) => (
                    <tr key={c.numero}>
                      <td style={{ ...TD, fontVariantNumeric: 'tabular-nums', fontWeight: 600 }}>{c.numero}</td>
                      <td style={{ ...TD, textAlign: 'right' }}>{c.lideres} líderes</td>
                      <td style={{ ...TD, textAlign: 'right' }}>{c.titulares} titulares</td>
                      <td style={{ ...TD, color: 'var(--z-text-muted)', whiteSpace: 'normal' }}>{c.lideresNombres}</td>
                    </tr>
                  ))}
                </tbody>
              </table>
            </Colapsable>
          )}

          {resultado.muestraNoEncontrados.length > 0 && (
            <Colapsable
              titulo={`Muestra de no encontrados (${resultado.muestraNoEncontrados.length} de ${num(resultado.numerosNoEncontrados)})`}
              abierto={verNoEncontrados}
              onToggle={() => setVerNoEncontrados((v) => !v)}
            >
              <div style={{ display: 'flex', flexWrap: 'wrap', gap: 6, padding: '10px 0' }}>
                {resultado.muestraNoEncontrados.map((n) => (
                  <span key={n} style={{
                    fontSize: 12, padding: '3px 8px', borderRadius: 4,
                    background: 'var(--z-bg-page)', border: '1px solid var(--z-border)',
                    fontVariantNumeric: 'tabular-nums',
                  }}>
                    {n}
                  </span>
                ))}
              </div>
            </Colapsable>
          )}

          <div>
            <ZButton variant="ghost" onClick={reiniciar}>Analizar otros archivos</ZButton>
          </div>
        </>
      )}
    </div>
  );
}

// ── Sub-componentes ─────────────────────────────────────────────────────────

function Colapsable({ titulo, abierto, onToggle, children }: {
  titulo: string; abierto: boolean; onToggle: () => void; children: React.ReactNode;
}) {
  return (
    <div style={{ border: '1px solid var(--z-border)', borderRadius: 10, overflow: 'hidden' }}>
      <button
        onClick={onToggle}
        style={{
          width: '100%', display: 'flex', alignItems: 'center', justifyContent: 'space-between',
          padding: '10px 16px', background: 'var(--z-bg-page)', border: 'none',
          cursor: 'pointer', fontSize: 13, fontWeight: 600, color: 'var(--z-text-primary)',
        }}
      >
        {titulo}
        <span style={{ fontSize: 10, color: 'var(--z-text-muted)' }}>{abierto ? '▲' : '▼'}</span>
      </button>
      {abierto && <div style={{ padding: '0 16px', overflowX: 'auto' }}>{children}</div>}
    </div>
  );
}

function TablaArchivos({ resultado }: { resultado: UsoBaseDatosDto }) {
  return (
    <div style={{ display: 'flex', flexDirection: 'column', gap: 10 }}>
      <p style={{ fontSize: 14, fontWeight: 600, color: 'var(--z-text-primary)', margin: 0 }}>
        Archivos procesados
        {resultado.solapamientoEntreArchivos > 0 && (
          <span style={{ fontSize: 12, fontWeight: 400, color: 'var(--z-text-muted)', marginLeft: 8 }}>
            — {num(resultado.solapamientoEntreArchivos)} número(s) venían repetidos entre archivos y se
            contaron una sola vez
          </span>
        )}
      </p>
      <div style={{ overflowX: 'auto', border: '1px solid var(--z-border)', borderRadius: 10 }}>
        <table style={{ width: '100%', borderCollapse: 'collapse', fontSize: 13 }}>
          <thead>
            <tr>
              {['Archivo', 'Columna', 'Filas', 'Válidos', 'Inválidos', 'Únicos'].map((h, i) => (
                <th key={h} style={{
                  padding: '10px 14px', fontSize: 11, fontWeight: 600, letterSpacing: '0.04em',
                  textTransform: 'uppercase', whiteSpace: 'nowrap', color: 'var(--z-text-primary)',
                  background: 'var(--z-brand-light)', borderBottom: '2px solid var(--z-brand)',
                  textAlign: i < 2 ? 'left' : 'right',
                }}>
                  {h}
                </th>
              ))}
            </tr>
          </thead>
          <tbody>
            {resultado.archivos.map((a) => (
              <tr key={a.nombreArchivo} style={{ background: a.ok ? 'transparent' : '#fef2f2' }}>
                <td style={{ ...TD, color: a.ok ? 'var(--z-text-primary)' : '#b91c1c' }}>
                  {a.nombreArchivo}
                  {!a.ok && a.mensaje && (
                    <div style={{ fontSize: 11, color: '#b91c1c', whiteSpace: 'normal', marginTop: 2 }}>
                      {a.mensaje}
                    </div>
                  )}
                  {a.ok && a.mensaje && (
                    <div style={{ fontSize: 11, color: '#92400e', whiteSpace: 'normal', marginTop: 2 }}>
                      {a.mensaje}
                    </div>
                  )}
                </td>
                <td style={{ ...TD, color: 'var(--z-text-muted)' }}>{a.columnaDetectada ?? '—'}</td>
                <td style={{ ...TD, textAlign: 'right', fontVariantNumeric: 'tabular-nums' }}>{num(a.filasLeidas)}</td>
                <td style={{ ...TD, textAlign: 'right', fontVariantNumeric: 'tabular-nums' }}>{num(a.telefonosValidos)}</td>
                <td style={{
                  ...TD, textAlign: 'right', fontVariantNumeric: 'tabular-nums',
                  color: a.telefonosInvalidos > 0 ? C.naranja : 'var(--z-text-muted)',
                }}>
                  {num(a.telefonosInvalidos)}
                </td>
                <td style={{ ...TD, textAlign: 'right', fontVariantNumeric: 'tabular-nums', fontWeight: 600 }}>
                  {num(a.telefonosDistintos)}
                </td>
              </tr>
            ))}
          </tbody>
        </table>
      </div>
    </div>
  );
}

function TablaPorFuente({ filas }: { filas: UsoPorFuente[] }) {
  const { sorted, sortKey, sortDir, handleSort } =
    useSortable<UsoPorFuente & Record<string, unknown>>(filas as (UsoPorFuente & Record<string, unknown>)[]);

  return (
    <div style={{ display: 'flex', flexDirection: 'column', gap: 10 }}>
      <p style={{ fontSize: 14, fontWeight: 600, color: 'var(--z-text-primary)', margin: 0 }}>
        Uso por fuente
      </p>
      <div style={{ overflowX: 'auto', border: '1px solid var(--z-border)', borderRadius: 10 }}>
        <table style={{ width: '100%', borderCollapse: 'collapse', fontSize: 13 }}>
          <thead>
            <tr>
              <ThSort label="Fuente" k="etiqueta" sortKey={sortKey as string | null} sortDir={sortDir} onSort={handleSort as (k: string) => void} align="left" />
              <ThSort label="Números distintos" k="numerosDistintos" sortKey={sortKey as string | null} sortDir={sortDir} onSort={handleSort as (k: string) => void} />
              <ThSort label="% del total" k="porcentaje" sortKey={sortKey as string | null} sortDir={sortDir} onSort={handleSort as (k: string) => void} />
              <ThSort label="Titulares" k="titularesDistintos" sortKey={sortKey as string | null} sortDir={sortDir} onSort={handleSort as (k: string) => void} />
              <ThSort label="Apariciones" k="apariciones" sortKey={sortKey as string | null} sortDir={sortDir} onSort={handleSort as (k: string) => void} />
            </tr>
          </thead>
          <tbody>
            {sorted.map((f) => (
              <tr
                key={f.etiqueta}
                onMouseEnter={(e) => { e.currentTarget.style.background = 'var(--z-bg-page)'; }}
                onMouseLeave={(e) => { e.currentTarget.style.background = 'transparent'; }}
              >
                <td style={TD}>{f.etiqueta}</td>
                <td style={{ ...TD, textAlign: 'right', fontVariantNumeric: 'tabular-nums', fontWeight: 600 }}>
                  {num(f.numerosDistintos)}
                </td>
                <td style={{ ...TD, textAlign: 'right', fontVariantNumeric: 'tabular-nums', color: 'var(--z-text-muted)' }}>
                  {f.porcentaje.toFixed(2)}%
                </td>
                <td style={{ ...TD, textAlign: 'right', fontVariantNumeric: 'tabular-nums' }}>
                  {num(f.titularesDistintos)}
                </td>
                <td style={{ ...TD, textAlign: 'right', fontVariantNumeric: 'tabular-nums' }}>
                  {num(f.apariciones)}
                </td>
              </tr>
            ))}
            {sorted.length === 0 && (
              <tr>
                <td colSpan={5} style={{ ...TD, textAlign: 'center', color: 'var(--z-text-muted)', padding: '24px 12px' }}>
                  Ninguno de los números cargados existe en las fuentes de localización.
                </td>
              </tr>
            )}
          </tbody>
        </table>
      </div>
    </div>
  );
}
