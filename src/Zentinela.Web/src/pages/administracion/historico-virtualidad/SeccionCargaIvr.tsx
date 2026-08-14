import { useRef, useState } from 'react';
import { useMutation } from '@tanstack/react-query';
import { CargandoSpiral } from '../../../components/CargandoSpiral';
import { PanelNombresValidos } from '../../../components/PanelNombresValidos';
import { validarCargaIvr, confirmarCargaIvr } from '../../../api/historicoVirtualidadApi';
import type {
  CatalogoCanal, ValidarCargaIvrResponse, ConfirmarCargaIvrResponse,
} from '../../../types/historicoVirtualidad';
import {
  TH, TD, ChipConteo, DropZone, ListaErroresAgrupados,
  btnPrimario, btnSecundario, cajaError, type Etapa,
} from './compartidos';

const num = (v: number) => v.toLocaleString('es-CO');

/** Qué panel de detalle está abierto (los chips son excluyentes entre sí). */
type Panel = 'errores' | 'desconocidos' | 'solapamiento' | 'alertas' | null;

export function SeccionCargaIvr({ catalogo }: { catalogo: CatalogoCanal }) {
  const inputRef = useRef<HTMLInputElement>(null);

  const [etapa, setEtapa]       = useState<Etapa>('inicial');
  // Se guarda el File (no solo su nombre): al confirmar se reenvía el mismo archivo.
  const [archivo, setArchivo]   = useState<File | null>(null);
  const [preview, setPreview]   = useState<ValidarCargaIvrResponse | null>(null);
  const [resultado, setResultado] = useState<ConfirmarCargaIvrResponse | null>(null);
  const [error, setError]       = useState('');
  const [panel, setPanel]       = useState<Panel>(null);
  const [progreso, setProgreso] = useState(0);

  const accept = catalogo.extensionesAceptadas.join(',') || '.csv,.xlsx';

  const mutValidar = useMutation({
    mutationFn: (f: File) => validarCargaIvr(f, setProgreso),
    onSuccess: (data) => {
      setPreview(data);
      setError('');
      setPanel(data.totalErrores > 0 ? 'errores' : null);
      setEtapa('preview');
    },
    onError: (err: unknown) => setError(mensajeDeError(err, 'No se pudo validar el archivo.')),
  });

  const mutConfirmar = useMutation({
    mutationFn: () => confirmarCargaIvr(archivo!, preview!.huellaArchivo, setProgreso),
    onSuccess: (data) => { setResultado(data); setEtapa('resultado'); },
    onError: (err: unknown) => setError(mensajeDeError(err, 'No se pudo cargar el archivo.')),
  });

  const seleccionar = (e: React.ChangeEvent<HTMLInputElement>) => {
    const f = e.target.files?.[0];
    if (!f) return;
    setArchivo(f);
    setError('');
    setProgreso(0);
    mutValidar.mutate(f);
  };

  const reiniciar = () => {
    setEtapa('inicial'); setArchivo(null); setPreview(null);
    setResultado(null); setError(''); setPanel(null); setProgreso(0);
    if (inputRef.current) inputRef.current.value = '';
  };

  if (mutValidar.isPending || mutConfirmar.isPending) {
    return (
      <CargandoSpiral
        mensaje={
          (mutValidar.isPending ? 'Validando el archivo' : 'Cargando los registros') +
          (progreso > 0 && progreso < 100 ? ` — subiendo ${progreso}%` : '…')
        }
      />
    );
  }

  return (
    <div style={{ display: 'flex', flexDirection: 'column', gap: 20 }}>
      {error && <div style={cajaError}>{error}</div>}

      {etapa === 'inicial' && (
        <>
          <div style={{ display: 'flex', flexDirection: 'column', gap: 6 }}>
            <p style={{ fontSize: 13, fontWeight: 600, color: 'var(--z-text-primary)', margin: 0 }}>
              Cargar resultados de IVR
            </p>
            <p style={{ fontSize: 12, color: 'var(--z-text-muted)', margin: 0, lineHeight: 1.5 }}>
              El archivo del marcador ya trae el resultado de cada llamada, así que se carga en un
              solo paso. Debe incluir las columnas{' '}
              <strong>{catalogo.columnasEsperadas.join(', ')}</strong> — se identifican por el nombre
              del encabezado sin importar mayúsculas, tildes ni el orden, y se aceptan columnas
              adicionales.
            </p>
          </div>

          <TablaMapeoEstados catalogo={catalogo} />

          <DropZone
            archivoNombre={archivo?.name ?? ''}
            archivoKB={(archivo?.size ?? 0) / 1024}
            onClick={() => inputRef.current?.click()}
            onQuitar={reiniciar}
            inputRef={inputRef}
            onChange={seleccionar}
            accept={accept}
            textoVacio={`Haz clic para seleccionar un archivo ${catalogo.extensionesAceptadas.join(' o ')}`}
          />

          <PanelNombresValidos />
        </>
      )}

      {etapa === 'preview' && preview && (
        <>
          {preview.columnasFaltantes.length > 0 && (
            <div style={cajaError}>
              <strong>Faltan columnas obligatorias:</strong> {preview.columnasFaltantes.join(', ')}.
              <div style={{ marginTop: 6, fontSize: 12 }}>
                Columnas encontradas en el archivo: {preview.columnasEncontradas.join(', ') || '(ninguna)'}
              </div>
            </div>
          )}

          {/* Los códigos no reconocidos van PRIMERO y en su propia caja: esas filas no se
              cargan, y el Informe de Virtualidad IVR mide volumen, así que perderlas
              subestima el informe. Debe ser imposible confirmar sin haberlo visto. */}
          {preview.totalCodigoDesconocido > 0 && (
            <div style={{
              background: '#fffbeb', border: '1px solid #fbbf24', borderRadius: 8,
              padding: '12px 16px', fontSize: 13, color: '#92400e', lineHeight: 1.5,
            }}>
              <strong>{num(preview.totalCodigoDesconocido)} fila(s) NO se cargarán</strong> porque su
              "Last App" no está en el catálogo de estados:{' '}
              <strong>{preview.codigosDesconocidos.join(', ')}</strong>.
              <div style={{ marginTop: 4 }}>
                Si el marcador agregó un código nuevo, hay que sumarlo al catálogo antes de cargar,
                o esas llamadas no quedarán en el informe.
              </div>
            </div>
          )}

          <div style={{ display: 'flex', gap: 12, flexWrap: 'wrap' }}>
            <ChipConteo
              numero={preview.totalValidas} label="registros válidos"
              bg="#f0fdf4" border="#86efac" colorNum="#16a34a" colorLabel="#15803d"
            />
            {preview.totalErrores > 0 && (
              <ChipConteo
                numero={preview.totalErrores} label="con error — no se cargarán"
                bg="#fef2f2" border="#fca5a5" colorNum="#dc2626" colorLabel="#b91c1c"
                onClick={() => setPanel(p => (p === 'errores' ? null : 'errores'))}
                abierto={panel === 'errores'}
              />
            )}
            {preview.solapamientos.length > 0 && (
              <ChipConteo
                numero={preview.solapamientos.reduce((s, x) => s + x.yaEnBaseDatos, 0)}
                label="ya cargados para estas carteras y fechas"
                bg="#fffbeb" border="#fbbf24" colorNum="#d97706" colorLabel="#92400e"
                onClick={() => setPanel(p => (p === 'solapamiento' ? null : 'solapamiento'))}
                abierto={panel === 'solapamiento'}
              />
            )}
            {preview.alertas.length > 0 && (
              <ChipConteo
                numero={preview.alertas.length} label="líneas saturadas"
                bg="#fffbeb" border="#fbbf24" colorNum="#d97706" colorLabel="#92400e"
                onClick={() => setPanel(p => (p === 'alertas' ? null : 'alertas'))}
                abierto={panel === 'alertas'}
              />
            )}
          </div>

          {preview.fechaMin && preview.fechaMax && (
            <p style={{ fontSize: 13, color: 'var(--z-text-muted)', margin: 0 }}>
              Fechas del archivo: <strong>{preview.fechaMin}</strong> → <strong>{preview.fechaMax}</strong>
              {preview.filasRepetidasEnArchivo > 0 && (
                <> · {num(preview.filasRepetidasEnArchivo)} fila(s) idénticas repetidas dentro del archivo</>
              )}
            </p>
          )}

          {panel === 'errores'      && <ListaErroresAgrupados errores={preview.erroresAgrupados} />}
          {panel === 'solapamiento' && <PanelSolapamiento preview={preview} />}
          {panel === 'alertas'      && <PanelAlertas preview={preview} />}

          {preview.totalValidas > 0 && (
            <>
              <DesglosePorEstado preview={preview} />
              <DesglosePorCartera preview={preview} />
              <MuestraFilas preview={preview} />
            </>
          )}

          <div style={{ display: 'flex', gap: 10 }}>
            <button
              onClick={() => mutConfirmar.mutate()}
              disabled={preview.totalValidas === 0}
              style={btnPrimario(preview.totalValidas > 0)}
            >
              {preview.totalErrores > 0
                ? `Cargar ${num(preview.totalValidas)} registros (${num(preview.totalErrores)} no se cargarán)`
                : `Cargar ${num(preview.totalValidas)} registros`}
            </button>
            <button onClick={reiniciar} style={btnSecundario}>Cancelar</button>
          </div>
        </>
      )}

      {etapa === 'resultado' && resultado && (
        <>
          <div style={{
            background: '#f0fdf4', border: '1px solid #86efac', borderRadius: 8,
            padding: '16px 20px', fontSize: 14, color: '#15803d',
          }}>
            <strong>{num(resultado.insertados)}</strong> registros de IVR cargados.
            {resultado.descartados > 0 && (
              <> {num(resultado.descartados)} fila(s) se descartaron por errores.</>
            )}
            <div style={{ fontSize: 12, color: '#166534', marginTop: 6 }}>
              Sello del lote: <code>{resultado.fechaCargue}</code> — identifica estas filas por si
              hay que revertir el cargue.
            </div>
          </div>
          <div>
            <button onClick={reiniciar} style={btnSecundario}>Cargar otro archivo</button>
          </div>
        </>
      )}
    </div>
  );
}

// ── Sub-componentes ─────────────────────────────────────────────────────────

/** Catálogo de traducción, visible antes de cargar para que se pueda auditar el mapeo. */
function TablaMapeoEstados({ catalogo }: { catalogo: CatalogoCanal }) {
  const [abierto, setAbierto] = useState(false);
  if (catalogo.mapeoEstados.length === 0) return null;

  return (
    <div style={{ border: '1px solid var(--z-border)', borderRadius: 8, overflow: 'hidden' }}>
      <button
        onClick={() => setAbierto(a => !a)}
        style={{
          width: '100%', display: 'flex', justifyContent: 'space-between', alignItems: 'center',
          padding: '10px 16px', background: 'var(--z-bg-page)', border: 'none',
          cursor: 'pointer', fontSize: 13, fontWeight: 600, color: 'var(--z-text-primary)',
        }}
      >
        Cómo se traduce la columna "Last App" ({catalogo.mapeoEstados.length} códigos)
        <span style={{ fontSize: 10, color: 'var(--z-text-muted)' }}>{abierto ? '▲' : '▼'}</span>
      </button>
      {abierto && (
        <div style={{ overflowX: 'auto' }}>
          <table style={{ width: '100%', borderCollapse: 'collapse' }}>
            <thead>
              <tr>
                <th style={TH}>Código en el archivo</th>
                <th style={TH}>Se guarda como</th>
                <th style={TH}>Significado</th>
              </tr>
            </thead>
            <tbody>
              {catalogo.mapeoEstados.map(m => (
                <tr key={m.codigoCrudo}>
                  <td style={{ ...TD, fontFamily: 'monospace' }}>{m.codigoCrudo}</td>
                  <td style={{ ...TD, fontWeight: 600 }}>{m.estado}</td>
                  <td style={{ ...TD, whiteSpace: 'normal', color: 'var(--z-text-muted)' }}>{m.significado}</td>
                </tr>
              ))}
            </tbody>
          </table>
        </div>
      )}
    </div>
  );
}

function DesglosePorEstado({ preview }: { preview: ValidarCargaIvrResponse }) {
  return (
    <div style={{ display: 'flex', flexDirection: 'column', gap: 8 }}>
      <p style={{ fontSize: 13, fontWeight: 600, color: 'var(--z-text-primary)', margin: 0 }}>
        Resultados del archivo
      </p>
      <div style={{ overflowX: 'auto', border: '1px solid var(--z-border)', borderRadius: 8 }}>
        <table style={{ width: '100%', borderCollapse: 'collapse' }}>
          <thead>
            <tr>
              <th style={TH}>Estado</th>
              <th style={TH}>Código (Last App)</th>
              <th style={{ ...TH, textAlign: 'right' }}>Llamadas</th>
              <th style={{ ...TH, textAlign: 'right' }}>%</th>
            </tr>
          </thead>
          <tbody>
            {preview.desglosePorEstado.map(e => (
              <tr key={`${e.estado}-${e.codigoCrudo}`}>
                <td style={{ ...TD, fontWeight: 600 }}>{e.estado}</td>
                {/* El código crudo al lado del estado traducido es lo que permite
                    auditar el mapeo de un vistazo. */}
                <td style={{ ...TD, fontFamily: 'monospace', color: 'var(--z-text-muted)' }}>{e.codigoCrudo}</td>
                <td style={{ ...TD, textAlign: 'right', fontVariantNumeric: 'tabular-nums' }}>{num(e.cantidad)}</td>
                <td style={{ ...TD, textAlign: 'right', fontVariantNumeric: 'tabular-nums', color: 'var(--z-text-muted)' }}>
                  {e.porcentaje.toFixed(2)}%
                </td>
              </tr>
            ))}
          </tbody>
        </table>
      </div>
    </div>
  );
}

function DesglosePorCartera({ preview }: { preview: ValidarCargaIvrResponse }) {
  return (
    <div style={{ display: 'flex', flexDirection: 'column', gap: 8 }}>
      <p style={{ fontSize: 13, fontWeight: 600, color: 'var(--z-text-primary)', margin: 0 }}>
        Distribución por cartera
      </p>
      <div style={{ overflowX: 'auto', border: '1px solid var(--z-border)', borderRadius: 8 }}>
        <table style={{ width: '100%', borderCollapse: 'collapse' }}>
          <thead>
            <tr>
              <th style={TH}>Cartera</th>
              <th style={TH}>Líder</th>
              <th style={{ ...TH, textAlign: 'right' }}>Llamadas</th>
            </tr>
          </thead>
          <tbody>
            {preview.desglosePorCartera.map(c => (
              <tr key={`${c.nombreCartera}-${c.nombreLider}`}>
                <td style={TD}>{c.nombreCartera}</td>
                <td style={TD}>{c.nombreLider}</td>
                <td style={{ ...TD, textAlign: 'right', fontVariantNumeric: 'tabular-nums' }}>{num(c.cantidad)}</td>
              </tr>
            ))}
          </tbody>
        </table>
      </div>
    </div>
  );
}

function MuestraFilas({ preview }: { preview: ValidarCargaIvrResponse }) {
  return (
    <div style={{ display: 'flex', flexDirection: 'column', gap: 8 }}>
      <p style={{ fontSize: 12, color: 'var(--z-text-muted)', margin: 0 }}>
        Muestra — primeros {preview.preview.length} registros de {num(preview.totalFilas)}
      </p>
      <div style={{ overflowX: 'auto', border: '1px solid var(--z-border)', borderRadius: 8 }}>
        <table style={{ width: '100%', borderCollapse: 'collapse' }}>
          <thead>
            <tr>
              <th style={TH}>Documento</th>
              <th style={TH}>Número</th>
              <th style={TH}>Cartera</th>
              <th style={TH}>Líder</th>
              <th style={TH}>Fecha</th>
              <th style={TH}>Last App</th>
              <th style={TH}>Estado</th>
            </tr>
          </thead>
          <tbody>
            {preview.preview.map((f, i) => (
              <tr key={i} style={{ background: f.esValida ? 'transparent' : '#fef2f2' }}>
                <td style={TD}>{f.numeroDocumento || '—'}</td>
                <td style={TD}>{f.destino || '—'}</td>
                <td style={TD}>{f.nombreCartera || '—'}</td>
                <td style={TD}>{f.nombreLider || '—'}</td>
                <td style={TD}>{f.esValida ? f.fechaEnvio : '—'}</td>
                <td style={{ ...TD, fontFamily: 'monospace' }}>{f.codigoCrudo || '—'}</td>
                <td style={{ ...TD, whiteSpace: 'normal' }}>
                  {f.esValida
                    ? <span style={{ color: '#16a34a', fontWeight: 600 }}>{f.estado}</span>
                    : <span style={{ color: '#dc2626' }} title={f.errorMensaje ?? ''}>{f.errorMensaje}</span>}
                </td>
              </tr>
            ))}
          </tbody>
        </table>
      </div>
    </div>
  );
}

function PanelSolapamiento({ preview }: { preview: ValidarCargaIvrResponse }) {
  return (
    <div style={{ background: '#fffbeb', border: '1px solid #fbbf24', borderRadius: 8, padding: '12px 16px' }}>
      <p style={{ margin: '0 0 8px', fontSize: 12, fontWeight: 600, color: '#92400e' }}>
        Ya hay registros de IVR para estas carteras en el rango de fechas del archivo.
        Si este archivo ya se cargó antes, confirmar lo duplicará.
      </p>
      <table style={{ width: '100%', borderCollapse: 'collapse' }}>
        <thead>
          <tr>
            <th style={TH}>Cartera</th>
            <th style={{ ...TH, textAlign: 'right' }}>En el archivo</th>
            <th style={{ ...TH, textAlign: 'right' }}>Ya en base de datos</th>
          </tr>
        </thead>
        <tbody>
          {preview.solapamientos.map(s => (
            <tr key={s.carteraID}>
              <td style={TD}>{s.nombreCartera}</td>
              <td style={{ ...TD, textAlign: 'right', fontVariantNumeric: 'tabular-nums' }}>{num(s.enArchivo)}</td>
              <td style={{ ...TD, textAlign: 'right', fontVariantNumeric: 'tabular-nums' }}>{num(s.yaEnBaseDatos)}</td>
            </tr>
          ))}
        </tbody>
      </table>
    </div>
  );
}

function PanelAlertas({ preview }: { preview: ValidarCargaIvrResponse }) {
  return (
    <div style={{ background: '#fffbeb', border: '1px solid #fbbf24', borderRadius: 8, padding: '12px 16px' }}>
      <p style={{ margin: '0 0 8px', fontSize: 12, fontWeight: 600, color: '#92400e' }}>
        Líneas marcadas muchas veces en este archivo
      </p>
      <ul style={{ margin: 0, paddingLeft: 20, display: 'flex', flexDirection: 'column', gap: 4 }}>
        {preview.alertas.map((a, i) => (
          <li key={i} style={{ fontSize: 13, color: '#92400e' }}>
            {a.destino} — {a.nombreCartera} ({a.totalEnvios} marcaciones)
          </li>
        ))}
      </ul>
    </div>
  );
}

function mensajeDeError(err: unknown, porDefecto: string): string {
  const e = err as { response?: { status?: number; data?: { mensaje?: string } } };
  const mensaje = e?.response?.data?.mensaje;
  if (mensaje) return mensaje;
  if (e?.response?.status === 409)
    return 'El archivo cambió desde la validación. Selecciónalo de nuevo para validarlo.';
  return porDefecto;
}
