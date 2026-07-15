import { useRef, useState } from 'react';
import { useMutation, useQuery } from '@tanstack/react-query';
import { obtenerCarteras } from '../../api/carterasApi';
import { CargandoSpiral } from '../../components/CargandoSpiral';
import {
  generarPlantillaSolicitud, validarSolicitud, confirmarSolicitud,
  descargarPendientes, validarResultado, confirmarResultado,
} from '../../api/solicitudesVirtualidadApi';
import type {
  CanalVirtualidad, FilaSolicitud, ValidarSolicitudResponse,
  ValidarResultadoResponse,
} from '../../types/solicitudesVirtualidad';

// ── Metadatos de canal ──────────────────────────────────────────────────────

const CANALES: { canal: CanalVirtualidad; label: string }[] = [
  { canal: 'IVR',    label: 'IVR' },
  { canal: 'Correo', label: 'Correos' },
  { canal: 'SMS',    label: 'SMS' },
];

const ESTADOS_POR_CANAL: Record<CanalVirtualidad, string[]> = {
  IVR:    ['Contestada', 'No contestada', 'Buzón', 'Número errado'],
  SMS:    ['Entregado', 'No entregado'],
  Correo: ['Entregado', 'Rebotado', 'Abierto'],
};

const etiquetaDestino = (canal: CanalVirtualidad) => (canal === 'Correo' ? 'Email' : 'Teléfono');

type Etapa = 'inicial' | 'preview' | 'resultado';

// ── Estilos compartidos de tabla ──────────────────────────────────────────────

const TH: React.CSSProperties = {
  padding: '8px 10px', textAlign: 'left', fontSize: 11, fontWeight: 600,
  color: 'var(--z-text-muted)', borderBottom: '0.5px solid var(--z-border)',
  letterSpacing: '0.04em', textTransform: 'uppercase', whiteSpace: 'nowrap',
};
const TD: React.CSSProperties = {
  padding: '8px 10px', fontSize: 13, borderBottom: '0.5px solid var(--z-border)',
  whiteSpace: 'nowrap', color: 'var(--z-text-primary)',
};

function ChipConteo({ numero, label, bg, border, colorNum, colorLabel, onClick, abierto }: {
  numero: number; label: string; bg: string; border: string;
  colorNum: string; colorLabel: string; onClick?: () => void; abierto?: boolean;
}) {
  const clickeable = !!onClick;
  return (
    <div
      onClick={onClick}
      role={clickeable ? 'button' : undefined}
      tabIndex={clickeable ? 0 : undefined}
      style={{
        display: 'flex', alignItems: 'center', gap: 8, background: bg,
        border: `0.5px solid ${border}`, borderRadius: 8, padding: '12px 18px',
        cursor: clickeable ? 'pointer' : 'default', userSelect: 'none',
        boxShadow: clickeable && abierto ? `inset 0 0 0 1px ${border}` : 'none',
      }}
    >
      <span style={{ fontSize: 22, fontWeight: 700, color: colorNum }}>{numero}</span>
      <span style={{ fontSize: 13, color: colorLabel }}>{label}</span>
      {clickeable && (
        <span style={{ fontSize: 11, color: colorLabel, marginLeft: 4, transform: abierto ? 'rotate(180deg)' : 'none' }}>▾</span>
      )}
    </div>
  );
}

function DropZone({ archivoNombre, archivoKB, onClick, onQuitar, inputRef, onChange, disabled }: {
  archivoNombre: string; archivoKB: number; onClick: () => void; onQuitar: () => void;
  inputRef: React.RefObject<HTMLInputElement | null>; onChange: (e: React.ChangeEvent<HTMLInputElement>) => void;
  disabled?: boolean;
}) {
  return (
    <div style={{ display: 'flex', flexDirection: 'column', gap: 6 }}>
      <div
        onClick={() => !disabled && onClick()}
        style={{
          border: `1.5px dashed ${archivoNombre ? 'var(--z-brand)' : 'var(--z-border)'}`,
          borderRadius: 8, padding: 24, textAlign: 'center',
          cursor: disabled ? 'not-allowed' : 'pointer',
          background: archivoNombre ? '#eff6ff' : 'transparent', opacity: disabled ? 0.5 : 1,
        }}
      >
        <p style={{ fontSize: 13, color: archivoNombre ? 'var(--z-brand)' : 'var(--z-text-muted)', margin: '0 0 4px' }}>
          {archivoNombre || 'Haz clic para seleccionar un archivo .xlsx'}
        </p>
        {archivoNombre && <p style={{ fontSize: 11, color: 'var(--z-text-muted)', margin: 0 }}>{archivoKB.toFixed(1)} KB</p>}
      </div>
      <input ref={inputRef} type="file" accept=".xlsx" disabled={disabled} onChange={onChange} style={{ display: 'none' }} />
      {archivoNombre && (
        <button onClick={onQuitar} style={{ background: 'none', border: 'none', fontSize: 11, color: 'var(--z-text-muted)', cursor: 'pointer', padding: 0, width: 'fit-content' }}>
          × Quitar archivo
        </button>
      )}
    </div>
  );
}

const btnPrimario = (habilitado: boolean): React.CSSProperties => ({
  padding: '9px 20px', borderRadius: 6, border: 'none',
  background: habilitado ? 'var(--z-brand)' : 'var(--z-border)',
  color: habilitado ? '#fff' : 'var(--z-text-muted)',
  fontSize: 13, fontWeight: 600, cursor: habilitado ? 'pointer' : 'not-allowed',
});
const btnSecundario: React.CSSProperties = {
  padding: '9px 16px', borderRadius: 6, border: '0.5px solid var(--z-border)',
  background: 'var(--z-bg-page)', color: 'var(--z-text-muted)', fontSize: 13, cursor: 'pointer',
};
const cajaError: React.CSSProperties = {
  background: '#fef2f2', border: '0.5px solid #fca5a5', borderRadius: 6,
  padding: '10px 14px', fontSize: 13, color: '#dc2626',
};

// ── Sección Solicitud ─────────────────────────────────────────────────────────

function SeccionSolicitud({ canal }: { canal: CanalVirtualidad }) {
  const inputRef = useRef<HTMLInputElement>(null);
  const [etapa, setEtapa]           = useState<Etapa>('inicial');
  const [generando, setGenerando]   = useState(false);
  const [archivoNombre, setNombre]  = useState('');
  const [archivoKB, setKB]          = useState(0);
  const [preview, setPreview]       = useState<ValidarSolicitudResponse | null>(null);
  const [resultado, setResultado]   = useState<{ insertados: number; mensaje: string } | null>(null);
  const [error, setError]           = useState('');
  const [verErrores, setVerErrores] = useState(false);
  const [verAlertas, setVerAlertas] = useState(false);

  const mutValidar = useMutation({
    mutationFn: (archivo: File) => validarSolicitud(archivo, canal),
    onSuccess: (data) => {
      setPreview(data); setVerErrores(data.totalErrores > 0); setVerAlertas(false);
      setEtapa('preview'); setError('');
    },
    onError: () => setError('No se pudo validar el archivo. Verifica el formato.'),
  });
  const mutConfirmar = useMutation({
    mutationFn: () => confirmarSolicitud(canal, preview!.filasValidas),
    onSuccess: (data) => { setResultado(data); setEtapa('resultado'); },
    onError: () => setError('Ocurrió un error al confirmar.'),
  });

  const seleccionar = (e: React.ChangeEvent<HTMLInputElement>) => {
    const archivo = e.target.files?.[0];
    if (!archivo) return;
    setNombre(archivo.name); setKB(archivo.size / 1024); setError('');
    mutValidar.mutate(archivo);
  };
  const reiniciar = () => {
    setEtapa('inicial'); setPreview(null); setNombre(''); setKB(0);
    setResultado(null); setError(''); setVerErrores(false); setVerAlertas(false);
    if (inputRef.current) inputRef.current.value = '';
  };

  const cargando = mutValidar.isPending || mutConfirmar.isPending;
  if (cargando) return <CargandoSpiral mensaje={mutValidar.isPending ? 'Validando archivo...' : 'Registrando solicitudes...'} />;

  if (etapa === 'resultado' && resultado) {
    return (
      <div style={{ background: '#f0fdf4', border: '0.5px solid #86efac', borderRadius: 8, padding: 24, display: 'flex', flexDirection: 'column', gap: 16 }}>
        <p style={{ fontSize: 15, fontWeight: 600, color: '#15803d', margin: 0 }}>Solicitudes registradas</p>
        <div style={{ display: 'flex', alignItems: 'center', gap: 8, background: '#dcfce7', border: '0.5px solid #86efac', borderRadius: 8, padding: '12px 18px', width: 'fit-content' }}>
          <span style={{ fontSize: 22, fontWeight: 700, color: '#15803d' }}>{resultado.insertados.toLocaleString('es-CO')}</span>
          <span style={{ fontSize: 13, color: '#166534' }}>solicitudes {canal} registradas (Estado pendiente)</span>
        </div>
        <p style={{ fontSize: 13, color: '#166534', margin: 0 }}>{resultado.mensaje}</p>
        <button onClick={reiniciar} style={{ ...btnSecundario, border: '0.5px solid #86efac', background: '#fff', color: '#15803d', fontWeight: 600, width: 'fit-content' }}>Nueva carga</button>
      </div>
    );
  }

  if (etapa === 'preview' && preview) {
    return (
      <div style={{ display: 'flex', flexDirection: 'column', gap: 16 }}>
        <div style={{ display: 'flex', gap: 12, flexWrap: 'wrap' }}>
          <ChipConteo numero={preview.totalValidas} label="solicitudes válidas" bg="#f0fdf4" border="#86efac" colorNum="#16a34a" colorLabel="#15803d" />
          {preview.totalErrores > 0 && (
            <ChipConteo numero={preview.totalErrores} label="con error — no se cargarán" bg="#fef2f2" border="#fca5a5" colorNum="#dc2626" colorLabel="#b91c1c" onClick={() => setVerErrores(v => !v)} abierto={verErrores} />
          )}
          {preview.alertas.length > 0 && (
            <ChipConteo numero={preview.alertas.length} label={`líneas saturadas (${etiquetaDestino(canal).toLowerCase()})`} bg="#fefce8" border="#fde68a" colorNum="#b45309" colorLabel="#92400e" onClick={() => setVerAlertas(v => !v)} abierto={verAlertas} />
          )}
        </div>

        {verAlertas && preview.alertas.length > 0 && (
          <div style={{ background: '#fefce8', border: '1px solid #fde68a', borderRadius: 8, padding: '12px 16px' }}>
            <p style={{ margin: '0 0 8px', fontSize: 12, fontWeight: 600, color: '#92400e' }}>Destinos con envíos acumulados sobre el umbral</p>
            <ul style={{ margin: 0, paddingLeft: 20, display: 'flex', flexDirection: 'column', gap: 4 }}>
              {preview.alertas.map((a, i) => (
                <li key={i} style={{ fontSize: 13, color: '#92400e' }}>{a.destino} — {a.nombreCartera} <strong>({a.totalEnvios} envíos)</strong></li>
              ))}
            </ul>
          </div>
        )}

        <TablaPreviewSolicitud filas={preview.preview} canal={canal} soloErrores={verErrores} />

        <div style={{ display: 'flex', gap: 10, alignItems: 'center' }}>
          <button onClick={() => mutConfirmar.mutate()} disabled={preview.totalValidas === 0} style={btnPrimario(preview.totalValidas > 0)}>
            Confirmar {preview.totalValidas} solicitud{preview.totalValidas !== 1 ? 'es' : ''}
          </button>
          <button onClick={reiniciar} style={btnSecundario}>Cancelar</button>
        </div>
        {error && <div style={cajaError}>{error}</div>}
      </div>
    );
  }

  // Etapa inicial
  return (
    <div style={{ background: 'var(--z-bg-card)', border: '0.5px solid var(--z-border)', borderRadius: 8, padding: 24, display: 'flex', flexDirection: 'column', gap: 20 }}>
      <div style={{ display: 'flex', flexDirection: 'column', gap: 6 }}>
        <p style={{ fontSize: 11, fontWeight: 600, color: 'var(--z-text-muted)', textTransform: 'uppercase', letterSpacing: '0.04em', margin: 0 }}>Paso 1 — Descarga la plantilla</p>
        <button onClick={async () => { setGenerando(true); await generarPlantillaSolicitud(canal); setGenerando(false); }} disabled={generando}
          style={{ padding: '8px 16px', borderRadius: 6, border: '0.5px solid var(--z-border)', background: 'var(--z-bg-page)', color: 'var(--z-text-primary)', fontSize: 13, fontWeight: 600, width: 'fit-content', cursor: generando ? 'not-allowed' : 'pointer' }}>
          {generando ? 'Generando...' : 'Generar plantilla'}
        </button>
        <p style={{ fontSize: 12, color: 'var(--z-text-muted)', margin: 0 }}>
          Columnas: <strong>NumeroDocumento</strong>, <strong>{etiquetaDestino(canal)}</strong>, <strong>Cartera</strong>, <strong>Lider</strong>, <strong>FechaEnvio</strong>.
          Escribe el nombre de la <strong>Cartera</strong> y del <strong>Líder</strong> tal como están registrados (ej. líder <em>Carlos A Torres</em>). El par líder-cartera debe existir y estar activo.
        </p>
      </div>
      <div style={{ display: 'flex', flexDirection: 'column', gap: 6 }}>
        <p style={{ fontSize: 11, fontWeight: 600, color: 'var(--z-text-muted)', textTransform: 'uppercase', letterSpacing: '0.04em', margin: 0 }}>Paso 2 — Sube el archivo diligenciado</p>
        <DropZone archivoNombre={archivoNombre} archivoKB={archivoKB} inputRef={inputRef}
          onClick={() => inputRef.current?.click()} onChange={seleccionar}
          onQuitar={() => { setNombre(''); setKB(0); if (inputRef.current) inputRef.current.value = ''; }} />
      </div>
      {error && <div style={cajaError}>{error}</div>}
    </div>
  );
}

function TablaPreviewSolicitud({ filas, canal, soloErrores }: { filas: FilaSolicitud[]; canal: CanalVirtualidad; soloErrores: boolean }) {
  const visibles = soloErrores ? filas.filter(f => !f.esValida) : filas;
  if (visibles.length === 0) return null;
  return (
    <div style={{ background: 'var(--z-bg-card)', border: '0.5px solid var(--z-border)', borderRadius: 8, overflow: 'hidden' }}>
      <p style={{ padding: '10px 16px', fontSize: 12, color: 'var(--z-text-muted)', borderBottom: '0.5px solid var(--z-border)', margin: 0 }}>
        Muestra — primeros {visibles.length} registros
      </p>
      <div style={{ overflowX: 'auto' }}>
        <table style={{ width: '100%', borderCollapse: 'collapse' }}>
          <thead><tr style={{ background: 'var(--z-bg-page)' }}>
            <th style={TH}>Documento</th><th style={TH}>{etiquetaDestino(canal)}</th><th style={TH}>Cartera</th>
            <th style={TH}>Líder</th><th style={TH}>Fecha</th><th style={TH}>Estado</th>
          </tr></thead>
          <tbody>
            {visibles.map((f, i) => (
              <tr key={i}>
                <td style={TD}>{f.numeroDocumento || '—'}</td>
                <td style={TD}>{f.destino || '—'}</td>
                <td style={TD}>{f.nombreCartera || '—'}</td>
                <td style={TD}>{f.nombreLider || '—'}</td>
                <td style={TD}>{f.fechaEnvio || '—'}</td>
                <td style={TD}>
                  {f.esValida
                    ? <span style={{ color: '#16a34a', fontWeight: 600, fontSize: 12 }}>Válida</span>
                    : <span style={{ color: '#dc2626', fontSize: 12 }} title={f.errorMensaje ?? ''}>{f.errorMensaje ?? 'Error'}</span>}
                </td>
              </tr>
            ))}
          </tbody>
        </table>
      </div>
    </div>
  );
}

// ── Sección Resultado ─────────────────────────────────────────────────────────

function SeccionResultado({ canal }: { canal: CanalVirtualidad }) {
  const inputRef = useRef<HTMLInputElement>(null);
  const [carteraID, setCarteraID] = useState<number>(0);
  const [desde, setDesde]         = useState('');
  const [hasta, setHasta]         = useState('');
  const [descargando, setDescargando] = useState(false);
  const [etapa, setEtapa]         = useState<Etapa>('inicial');
  const [archivoNombre, setNombre] = useState('');
  const [archivoKB, setKB]        = useState(0);
  const [preview, setPreview]     = useState<ValidarResultadoResponse | null>(null);
  const [resultado, setResultado] = useState<{ actualizados: number; mensaje: string } | null>(null);
  const [error, setError]         = useState('');
  const [verErrores, setVerErrores] = useState(false);

  const { data: carteras = [] } = useQuery({ queryKey: ['carteras'], queryFn: obtenerCarteras });

  const mutValidar = useMutation({
    mutationFn: (archivo: File) => validarResultado(archivo, canal),
    onSuccess: (data) => { setPreview(data); setVerErrores(data.totalErrores > 0); setEtapa('preview'); setError(''); },
    onError: () => setError('No se pudo validar el archivo de resultados.'),
  });
  const mutConfirmar = useMutation({
    mutationFn: () => confirmarResultado(preview!.filasValidas),
    onSuccess: (data) => { setResultado(data); setEtapa('resultado'); },
    onError: () => setError('Ocurrió un error al confirmar los resultados.'),
  });

  const seleccionar = (e: React.ChangeEvent<HTMLInputElement>) => {
    const archivo = e.target.files?.[0];
    if (!archivo) return;
    setNombre(archivo.name); setKB(archivo.size / 1024); setError('');
    mutValidar.mutate(archivo);
  };
  const reiniciar = () => {
    setEtapa('inicial'); setPreview(null); setNombre(''); setKB(0);
    setResultado(null); setError(''); setVerErrores(false);
    if (inputRef.current) inputRef.current.value = '';
  };

  const cargando = mutValidar.isPending || mutConfirmar.isPending;
  if (cargando) return <CargandoSpiral mensaje={mutValidar.isPending ? 'Validando resultados...' : 'Actualizando resultados...'} />;

  if (etapa === 'resultado' && resultado) {
    return (
      <div style={{ background: '#f0fdf4', border: '0.5px solid #86efac', borderRadius: 8, padding: 24, display: 'flex', flexDirection: 'column', gap: 16 }}>
        <p style={{ fontSize: 15, fontWeight: 600, color: '#15803d', margin: 0 }}>Resultados actualizados</p>
        <div style={{ display: 'flex', alignItems: 'center', gap: 8, background: '#dcfce7', border: '0.5px solid #86efac', borderRadius: 8, padding: '12px 18px', width: 'fit-content' }}>
          <span style={{ fontSize: 22, fontWeight: 700, color: '#15803d' }}>{resultado.actualizados.toLocaleString('es-CO')}</span>
          <span style={{ fontSize: 13, color: '#166534' }}>solicitudes {canal} con Estado actualizado</span>
        </div>
        <p style={{ fontSize: 13, color: '#166534', margin: 0 }}>{resultado.mensaje}</p>
        <button onClick={reiniciar} style={{ ...btnSecundario, border: '0.5px solid #86efac', background: '#fff', color: '#15803d', fontWeight: 600, width: 'fit-content' }}>Cargar otro archivo</button>
      </div>
    );
  }

  if (etapa === 'preview' && preview) {
    const visibles = verErrores ? preview.preview.filter(f => !f.esValida) : preview.preview;
    return (
      <div style={{ display: 'flex', flexDirection: 'column', gap: 16 }}>
        <div style={{ display: 'flex', gap: 12, flexWrap: 'wrap' }}>
          <ChipConteo numero={preview.totalValidas} label="resultados válidos" bg="#f0fdf4" border="#86efac" colorNum="#16a34a" colorLabel="#15803d" />
          {preview.totalErrores > 0 && (
            <ChipConteo numero={preview.totalErrores} label="con error — no se aplicarán" bg="#fef2f2" border="#fca5a5" colorNum="#dc2626" colorLabel="#b91c1c" onClick={() => setVerErrores(v => !v)} abierto={verErrores} />
          )}
        </div>
        {visibles.length > 0 && (
          <div style={{ background: 'var(--z-bg-card)', border: '0.5px solid var(--z-border)', borderRadius: 8, overflow: 'hidden' }}>
            <div style={{ overflowX: 'auto' }}>
              <table style={{ width: '100%', borderCollapse: 'collapse' }}>
                <thead><tr style={{ background: 'var(--z-bg-page)' }}><th style={TH}>ID</th><th style={TH}>Estado</th><th style={TH}>Validación</th></tr></thead>
                <tbody>
                  {visibles.map((f, i) => (
                    <tr key={i}>
                      <td style={TD}>{f.id || '—'}</td>
                      <td style={TD}>{f.estado || '—'}</td>
                      <td style={TD}>{f.esValida
                        ? <span style={{ color: '#16a34a', fontWeight: 600, fontSize: 12 }}>OK</span>
                        : <span style={{ color: '#dc2626', fontSize: 12 }}>{f.errorMensaje ?? 'Error'}</span>}</td>
                    </tr>
                  ))}
                </tbody>
              </table>
            </div>
          </div>
        )}
        <div style={{ display: 'flex', gap: 10, alignItems: 'center' }}>
          <button onClick={() => mutConfirmar.mutate()} disabled={preview.totalValidas === 0} style={btnPrimario(preview.totalValidas > 0)}>
            Aplicar {preview.totalValidas} resultado{preview.totalValidas !== 1 ? 's' : ''}
          </button>
          <button onClick={reiniciar} style={btnSecundario}>Cancelar</button>
        </div>
        {error && <div style={cajaError}>{error}</div>}
      </div>
    );
  }

  // Etapa inicial
  return (
    <div style={{ background: 'var(--z-bg-card)', border: '0.5px solid var(--z-border)', borderRadius: 8, padding: 24, display: 'flex', flexDirection: 'column', gap: 20 }}>
      <div style={{ display: 'flex', flexDirection: 'column', gap: 6 }}>
        <p style={{ fontSize: 11, fontWeight: 600, color: 'var(--z-text-muted)', textTransform: 'uppercase', letterSpacing: '0.04em', margin: 0 }}>Paso 1 — Descarga las solicitudes pendientes</p>
        <div style={{ display: 'flex', gap: 10, flexWrap: 'wrap', alignItems: 'center' }}>
          <select value={carteraID} onChange={e => setCarteraID(Number(e.target.value))}
            style={{ padding: '7px 10px', borderRadius: 6, border: '0.5px solid var(--z-border)', background: 'var(--z-bg-page)', color: 'var(--z-text-primary)', fontSize: 13, width: 240, cursor: 'pointer' }}>
            <option value={0}>Todas las carteras</option>
            {carteras.map(c => <option key={c.id} value={c.id}>{c.nombreCartera}</option>)}
          </select>
          <input type="date" value={desde} onChange={e => setDesde(e.target.value)} title="Desde"
            style={{ padding: '7px 10px', borderRadius: 6, border: '0.5px solid var(--z-border)', background: 'var(--z-bg-page)', color: 'var(--z-text-primary)', fontSize: 13 }} />
          <input type="date" value={hasta} onChange={e => setHasta(e.target.value)} title="Hasta"
            style={{ padding: '7px 10px', borderRadius: 6, border: '0.5px solid var(--z-border)', background: 'var(--z-bg-page)', color: 'var(--z-text-primary)', fontSize: 13 }} />
          <button onClick={async () => { setDescargando(true); try { await descargarPendientes(canal, carteraID, desde, hasta); } finally { setDescargando(false); } }} disabled={descargando}
            style={{ padding: '8px 16px', borderRadius: 6, border: '0.5px solid var(--z-border)', background: 'var(--z-bg-page)', color: 'var(--z-text-primary)', fontSize: 13, fontWeight: 600, cursor: descargando ? 'not-allowed' : 'pointer' }}>
            {descargando ? 'Generando...' : 'Descargar pendientes'}
          </button>
        </div>
        <p style={{ fontSize: 12, color: 'var(--z-text-muted)', margin: 0 }}>
          El archivo trae la columna <strong>ID</strong> (no la modifiques) y una columna <strong>Estado</strong> vacía.
          Diligéncia el Estado con uno de: {ESTADOS_POR_CANAL[canal].map((e, i) => <em key={e}>{i > 0 ? ', ' : ''}{e}</em>)}.
        </p>
      </div>
      <div style={{ display: 'flex', flexDirection: 'column', gap: 6 }}>
        <p style={{ fontSize: 11, fontWeight: 600, color: 'var(--z-text-muted)', textTransform: 'uppercase', letterSpacing: '0.04em', margin: 0 }}>Paso 2 — Sube el archivo con los resultados</p>
        <DropZone archivoNombre={archivoNombre} archivoKB={archivoKB} inputRef={inputRef}
          onClick={() => inputRef.current?.click()} onChange={seleccionar}
          onQuitar={() => { setNombre(''); setKB(0); if (inputRef.current) inputRef.current.value = ''; }} />
      </div>
      {error && <div style={cajaError}>{error}</div>}
    </div>
  );
}

// ── Página ──────────────────────────────────────────────────────────────────

export function GestionSolicitudesVirtualidadPage() {
  const [canal, setCanal] = useState<CanalVirtualidad>('IVR');
  const [fase, setFase]   = useState<'solicitud' | 'resultado'>('solicitud');

  const tabBase: React.CSSProperties = {
    padding: '8px 16px', borderRadius: 6, border: '0.5px solid var(--z-border)',
    background: 'var(--z-bg-page)', color: 'var(--z-text-muted)', fontSize: 13, cursor: 'pointer',
    transition: 'background 0.18s ease, color 0.18s ease, transform 0.18s ease, border-color 0.18s ease',
  };
  const tabActivo: React.CSSProperties = { ...tabBase, background: 'var(--z-brand)', color: '#fff', border: '0.5px solid var(--z-brand)', fontWeight: 600 };
  const faseBase: React.CSSProperties = { padding: '6px 4px', border: 'none', background: 'none', fontSize: 14, cursor: 'pointer', color: 'var(--z-text-muted)' };
  const faseActiva: React.CSSProperties = { ...faseBase, color: 'var(--z-text-primary)', fontWeight: 600 };

  return (
    <div style={{ display: 'flex', flexDirection: 'column', gap: 24, maxWidth: 1000 }}>
      <style>{`
        .z-tab-canal:hover { background: var(--z-brand-light) !important; color: var(--z-brand) !important; border-color: var(--z-brand) !important; transform: translateY(-1px); }
        .z-tab-fase { position: relative; transition: color 0.2s ease; }
        .z-tab-fase::after {
          content: '';
          position: absolute;
          left: 50%; right: 50%; bottom: -1px; height: 2px;
          background: var(--z-brand);
          transition: left 0.22s ease, right 0.22s ease;
        }
        .z-tab-fase:hover { color: var(--z-brand); }
        .z-tab-fase:hover::after { left: 4px; right: 4px; }
        .z-tab-fase-activa::after,
        .z-tab-fase-activa:hover::after { left: 0; right: 0; }
      `}</style>
      <div>
        <h1 style={{ fontSize: 20, fontWeight: 600, color: 'var(--z-text-primary)', margin: 0 }}>Gestión de Solicitudes de Virtualidad</h1>
        <p style={{ fontSize: 13, color: 'var(--z-text-muted)', margin: '4px 0 0' }}>
          Registra el histórico de solicitudes IVR, SMS y Correos, y actualiza sus resultados. El mapeo a cartera y líder se resuelve por el nombre indicado en el archivo.
        </p>
      </div>

      {/* Tabs de canal */}
      <div style={{ display: 'flex', gap: 8, flexWrap: 'wrap' }}>
        {CANALES.map(c => (
          <button key={c.canal} onClick={() => { setCanal(c.canal); }}
            className={canal === c.canal ? undefined : 'z-tab-canal'}
            style={canal === c.canal ? tabActivo : tabBase}>{c.label}</button>
        ))}
      </div>

      {/* Sub-tabs de fase */}
      <div style={{ display: 'flex', gap: 20, borderBottom: '0.5px solid var(--z-border)' }}>
        <button onClick={() => setFase('solicitud')}
          className={`z-tab-fase${fase === 'solicitud' ? ' z-tab-fase-activa' : ''}`}
          style={fase === 'solicitud' ? faseActiva : faseBase}>Solicitud</button>
        <button onClick={() => setFase('resultado')}
          className={`z-tab-fase${fase === 'resultado' ? ' z-tab-fase-activa' : ''}`}
          style={fase === 'resultado' ? faseActiva : faseBase}>Resultado</button>
      </div>

      {/* key={canal+fase} reinicia el estado interno al cambiar de canal o fase */}
      {fase === 'solicitud'
        ? <SeccionSolicitud key={`sol-${canal}`} canal={canal} />
        : <SeccionResultado key={`res-${canal}`} canal={canal} />}
    </div>
  );
}
