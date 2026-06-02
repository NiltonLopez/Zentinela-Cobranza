import { useRef, useState } from 'react';
import { useQuery, useMutation } from '@tanstack/react-query';
import {
  listarCarterasPagos,
  validarCsvPagos,
  confirmarCarguePagos,
  generarPlantillaPagos,
  obtenerUltimaActualizacionPagos,
} from '../../api/pagosApi';
import { LeyendaUltimaActualizacion } from '../../components/LeyendaUltimaActualizacion';
import { CargandoSpiral } from '../../components/CargandoSpiral';
import type { PagoFila, ValidarPagosResponse } from '../../types/pagos';

// ── Helpers ───────────────────────────────────────────────────────────────────

type Etapa = 'inicial' | 'preview' | 'resultado';

const cop = (v: number) =>
  new Intl.NumberFormat('es-CO', { style: 'currency', currency: 'COP', maximumFractionDigits: 0 }).format(v);

// ── Badge de estado ───────────────────────────────────────────────────────────

function BadgePago({ esValida }: { esValida: boolean }) {
  if (esValida) {
    return (
      <span style={{
        display: 'inline-block', padding: '2px 8px', borderRadius: 99,
        fontSize: 11, fontWeight: 600,
        background: '#f0fdf4', color: '#16a34a', border: '1px solid #86efac',
      }}>
        Listo
      </span>
    );
  }
  return (
    <span style={{
      display: 'inline-block', padding: '2px 8px', borderRadius: 99,
      fontSize: 11, fontWeight: 600,
      background: '#fef2f2', color: '#dc2626', border: '1px solid #fca5a5',
    }}>
      Error
    </span>
  );
}

// ── Chip de conteo ────────────────────────────────────────────────────────────

function ChipConteo({
  numero, label, bg, border, colorNum, colorLabel,
  onClick, abierto,
}: {
  numero: number; label: string;
  bg: string; border: string; colorNum: string; colorLabel: string;
  onClick?: () => void;
  abierto?: boolean;
}) {
  const esClickeable = !!onClick;
  return (
    <div
      onClick={onClick}
      role={esClickeable ? 'button' : undefined}
      tabIndex={esClickeable ? 0 : undefined}
      onKeyDown={esClickeable
        ? (e) => { if (e.key === 'Enter' || e.key === ' ') { e.preventDefault(); onClick!(); } }
        : undefined}
      style={{
        display: 'flex', alignItems: 'center', gap: '8px',
        background: bg, border: `0.5px solid ${border}`,
        borderRadius: '8px', padding: '12px 18px',
        cursor: esClickeable ? 'pointer' : 'default',
        userSelect: 'none',
        boxShadow: esClickeable && abierto ? `inset 0 0 0 1px ${border}` : 'none',
        transition: 'box-shadow 0.15s, transform 0.05s',
      }}
      title={esClickeable ? (abierto ? 'Click para ocultar el detalle' : 'Click para ver el detalle') : undefined}
    >
      <span style={{ fontSize: '22px', fontWeight: 700, color: colorNum }}>
        {numero.toLocaleString('es-CO')}
      </span>
      <span style={{ fontSize: '13px', color: colorLabel }}>{label}</span>
      {esClickeable && (
        <span style={{
          fontSize: '11px', color: colorLabel, marginLeft: 4,
          transform: abierto ? 'rotate(180deg)' : 'rotate(0deg)',
          transition: 'transform 0.15s',
        }}>
          ▾
        </span>
      )}
    </div>
  );
}

// ── Panel de errores ──────────────────────────────────────────────────────────

function PanelErrores({ filas }: { filas: PagoFila[] }) {
  if (filas.length === 0) return null;

  return (
    <div style={{
      background: '#fef2f2', border: '1px solid #fca5a5',
      borderRadius: '8px', overflow: 'hidden',
    }}>
      {/* Encabezado */}
      <div style={{
        padding: '14px 18px', borderBottom: '1px solid #fca5a5',
        display: 'flex', alignItems: 'baseline', gap: '10px',
      }}>
        <span style={{ fontSize: 24, fontWeight: 700, color: '#dc2626', lineHeight: 1 }}>
          {filas.length.toLocaleString('es-CO')}
        </span>
        <p style={{ margin: 0, fontSize: 14, fontWeight: 600, color: '#b91c1c' }}>
          {filas.length === 1
            ? 'registro con error — no se cargará'
            : 'registros con error — no se cargarán'}
        </p>
      </div>

      {/* Tabla registro por registro */}
      <div style={{ overflowX: 'auto' }}>
        <table style={{ width: '100%', borderCollapse: 'collapse' }}>
          <thead>
            <tr style={{ background: '#fee2e2' }}>
              {['#', 'Cédula', 'Nombre', 'Usuario Control', 'Error'].map((col, i) => (
                <th key={i} style={{
                  padding: '8px 14px', textAlign: 'left',
                  fontSize: '11px', fontWeight: 600, color: '#b91c1c',
                  borderBottom: '1px solid #fca5a5',
                  letterSpacing: '0.04em', textTransform: 'uppercase', whiteSpace: 'nowrap',
                }}>
                  {col}
                </th>
              ))}
            </tr>
          </thead>
          <tbody>
            {filas.map((fila, idx) => (
              <tr
                key={idx}
                style={{
                  borderBottom: idx < filas.length - 1 ? '0.5px solid #fecaca' : 'none',
                  background: idx % 2 === 0 ? 'transparent' : '#fff5f5',
                }}
              >
                <td style={{ padding: '8px 14px', fontSize: '12px', color: '#b91c1c', whiteSpace: 'nowrap' }}>
                  {idx + 1}
                </td>
                <td style={{ padding: '8px 14px', fontSize: '13px', color: '#7f1d1d', fontFamily: 'monospace', whiteSpace: 'nowrap' }}>
                  {fila.cedulaTitular}
                </td>
                <td style={{ padding: '8px 14px', fontSize: '13px', color: '#7f1d1d', whiteSpace: 'nowrap' }}>
                  {fila.nombreCliente || '—'}
                </td>
                <td style={{ padding: '8px 14px', fontSize: '12px', color: '#b91c1c', fontFamily: 'monospace', whiteSpace: 'nowrap' }}>
                  {fila.usuarioControl || '—'}
                </td>
                <td style={{ padding: '8px 14px', fontSize: '13px', color: '#dc2626' }}>
                  {fila.errorMensaje ?? 'Error desconocido'}
                </td>
              </tr>
            ))}
          </tbody>
        </table>
      </div>
    </div>
  );
}

// ── Panel de duplicados (ya cargados — se omiten) ───────────────────────────────

function PanelDuplicados({ filas }: { filas: PagoFila[] }) {
  if (filas.length === 0) return null;

  return (
    <div style={{
      background: '#fffbeb', border: '1px solid #fcd34d',
      borderRadius: '8px', overflow: 'hidden',
    }}>
      {/* Encabezado */}
      <div style={{
        padding: '14px 18px', borderBottom: '1px solid #fcd34d',
        display: 'flex', alignItems: 'baseline', gap: '10px',
      }}>
        <span style={{ fontSize: 24, fontWeight: 700, color: '#b45309', lineHeight: 1 }}>
          {filas.length.toLocaleString('es-CO')}
        </span>
        <p style={{ margin: 0, fontSize: 14, fontWeight: 600, color: '#92400e' }}>
          {filas.length === 1
            ? 'pago ya cargado — se omitirá'
            : 'pagos ya cargados — se omitirán'}
        </p>
      </div>

      {/* Tabla registro por registro */}
      <div style={{ overflowX: 'auto' }}>
        <table style={{ width: '100%', borderCollapse: 'collapse' }}>
          <thead>
            <tr style={{ background: '#fef3c7' }}>
              {['#', 'Cédula', 'Nombre', 'Valor pagado', 'Fecha', 'Motivo'].map((col, i) => (
                <th key={i} style={{
                  padding: '8px 14px', textAlign: 'left',
                  fontSize: '11px', fontWeight: 600, color: '#92400e',
                  borderBottom: '1px solid #fcd34d',
                  letterSpacing: '0.04em', textTransform: 'uppercase', whiteSpace: 'nowrap',
                }}>
                  {col}
                </th>
              ))}
            </tr>
          </thead>
          <tbody>
            {filas.map((fila, idx) => (
              <tr
                key={idx}
                style={{
                  borderBottom: idx < filas.length - 1 ? '0.5px solid #fde68a' : 'none',
                  background: idx % 2 === 0 ? 'transparent' : '#fffaf0',
                }}
              >
                <td style={{ padding: '8px 14px', fontSize: '12px', color: '#92400e', whiteSpace: 'nowrap' }}>
                  {idx + 1}
                </td>
                <td style={{ padding: '8px 14px', fontSize: '13px', color: '#78350f', fontFamily: 'monospace', whiteSpace: 'nowrap' }}>
                  {fila.cedulaTitular}
                </td>
                <td style={{ padding: '8px 14px', fontSize: '13px', color: '#78350f', whiteSpace: 'nowrap' }}>
                  {fila.nombreCliente || '—'}
                </td>
                <td style={{ padding: '8px 14px', fontSize: '13px', color: '#78350f', textAlign: 'right', whiteSpace: 'nowrap' }}>
                  {cop(fila.valorPagado)}
                </td>
                <td style={{ padding: '8px 14px', fontSize: '13px', color: '#78350f', whiteSpace: 'nowrap' }}>
                  {fila.fechaPago}
                </td>
                <td style={{ padding: '8px 14px', fontSize: '13px', color: '#b45309' }}>
                  {fila.errorMensaje ?? 'Pago ya cargado'}
                </td>
              </tr>
            ))}
          </tbody>
        </table>
      </div>
    </div>
  );
}

// ── Tabla de preview ──────────────────────────────────────────────────────────

function TablaPreview({ filas, totalValidas }: { filas: PagoFila[]; totalValidas: number }) {
  if (filas.length === 0) return null;
  const COLS = ['#', 'Cédula', 'Nombre', 'Valor pagado', 'Fecha', 'Honorarios', 'Usuario Control', 'Estado'];
  return (
    <div style={{
      background: 'var(--z-bg-card)', border: '0.5px solid var(--z-border)',
      borderRadius: '8px', overflow: 'hidden',
    }}>
      <p style={{
        padding: '10px 16px', fontSize: '12px', color: 'var(--z-text-muted)',
        borderBottom: '0.5px solid var(--z-border)', margin: 0,
      }}>
        Muestra — primeros {filas.length} de {totalValidas.toLocaleString('es-CO')} registros válidos
      </p>
      <div style={{ overflowX: 'auto' }}>
        <table style={{ width: '100%', borderCollapse: 'collapse' }}>
          <thead>
            <tr style={{ background: 'var(--z-bg-page)' }}>
              {COLS.map((col, i) => (
                <th key={i} style={{
                  padding: '9px 14px', textAlign: 'left',
                  fontSize: '11px', fontWeight: 600, color: 'var(--z-text-muted)',
                  borderBottom: '0.5px solid var(--z-border)',
                  letterSpacing: '0.04em', textTransform: 'uppercase', whiteSpace: 'nowrap',
                }}>
                  {col}
                </th>
              ))}
            </tr>
          </thead>
          <tbody>
            {filas.map((fila, idx) => (
              <tr key={idx} style={{ borderBottom: '0.5px solid var(--z-border)' }}>
                <td style={{ padding: '9px 14px', fontSize: '13px', color: 'var(--z-text-muted)' }}>
                  {idx + 1}
                </td>
                <td style={{ padding: '9px 14px', fontSize: '13px', color: 'var(--z-text-primary)', fontWeight: 500 }}>
                  {fila.cedulaTitular}
                </td>
                <td style={{ padding: '9px 14px', fontSize: '13px', color: 'var(--z-text-muted)' }}>
                  {fila.nombreCliente}
                </td>
                <td style={{ padding: '9px 14px', fontSize: '13px', color: 'var(--z-text-primary)', textAlign: 'right' }}>
                  {cop(fila.valorPagado)}
                </td>
                <td style={{ padding: '9px 14px', fontSize: '13px', color: 'var(--z-text-primary)', whiteSpace: 'nowrap' }}>
                  {fila.fechaPago}
                </td>
                <td style={{ padding: '9px 14px', fontSize: '13px', color: 'var(--z-text-primary)', textAlign: 'right' }}>
                  {cop(fila.honorarios)}
                </td>
                <td style={{ padding: '9px 14px', fontSize: '13px', color: 'var(--z-text-muted)' }}>
                  {fila.esBaseGeneral ? (
                    <span style={{
                      fontSize: 11, fontWeight: 600, padding: '2px 8px',
                      borderRadius: 12, background: '#0EA5E922', color: '#0369A1',
                      whiteSpace: 'nowrap',
                    }}>
                      BASE GENERAL
                    </span>
                  ) : (
                    fila.usuarioControl
                  )}
                </td>
                <td style={{ padding: '9px 14px' }}>
                  <BadgePago esValida={fila.esValida} />
                </td>
              </tr>
            ))}
          </tbody>
        </table>
      </div>
    </div>
  );
}

// ── Componente principal ──────────────────────────────────────────────────────

export function CarguePagosPage() {
  const inputRef = useRef<HTMLInputElement>(null);

  const [etapa,              setEtapa]              = useState<Etapa>('inicial');
  const [carteraID,          setCarteraID]          = useState<number | ''>('');
  const [archivoNombre,      setArchivoNombre]      = useState('');
  const [archivoKB,          setArchivoKB]          = useState(0);
  const [preview,            setPreview]            = useState<ValidarPagosResponse | null>(null);
  const [insertados,         setInsertados]         = useState(0);
  const [errorMensaje,       setErrorMensaje]       = useState('');
  const [generandoPlantilla, setGenerandoPlantilla] = useState(false);

  // Toggles para los detalles asociados a cada chip de conteo
  const [verErrores,    setVerErrores]    = useState(false);
  const [verValidos,    setVerValidos]    = useState(false);
  const [verDuplicados, setVerDuplicados] = useState(false);

  const { data: carteras = [] } = useQuery({
    queryKey: ['pagos-carteras'],
    queryFn:  listarCarterasPagos,
  });

  const mutValidar = useMutation({
    mutationFn: () => validarCsvPagos(
      inputRef.current!.files![0],
      Number(carteraID),
    ),
    onSuccess: (data) => {
      setPreview(data);
      setErrorMensaje('');
      // Si hay errores o duplicados, abrir esos paneles por defecto para que sean visibles;
      // los válidos quedan colapsados (memory: vista previa máx. 10).
      setVerErrores(data.cantidadErrores > 0);
      setVerDuplicados(data.cantidadDuplicados > 0);
      setVerValidos(false);
      setEtapa('preview');
    },
    onError: (err: unknown) => {
      const msg = (err as { response?: { data?: { mensaje?: string } } })
        ?.response?.data?.mensaje ?? 'No se pudo validar el archivo. Verifica el formato.';
      setErrorMensaje(msg);
    },
  });

  const mutConfirmar = useMutation({
    mutationFn: () => confirmarCarguePagos(Number(carteraID), preview!.todasLasValidas),
    onSuccess: (data) => {
      setInsertados(data.insertados);
      setEtapa('resultado');
    },
    onError: (err: unknown) => {
      const msg = (err as { response?: { data?: { mensaje?: string } } })
        ?.response?.data?.mensaje ?? 'Ocurrió un error al confirmar el cargue.';
      setErrorMensaje(msg);
    },
  });

  const handleSeleccionarArchivo = (e: React.ChangeEvent<HTMLInputElement>) => {
    const archivo = e.target.files?.[0];
    if (!archivo) return;
    setArchivoNombre(archivo.name);
    setArchivoKB(archivo.size / 1024);
    setErrorMensaje('');
  };

  const handleNuevaCarga = () => {
    setEtapa('inicial');
    setPreview(null);
    setArchivoNombre('');
    setArchivoKB(0);
    setInsertados(0);
    setErrorMensaje('');
    setVerErrores(false);
    setVerValidos(false);
    setVerDuplicados(false);
    if (inputRef.current) inputRef.current.value = '';
  };

  const esCargando   = mutValidar.isPending || mutConfirmar.isPending;
  const puedeValidar = !!carteraID && !!archivoNombre;

  return (
    <div style={{ display: 'flex', flexDirection: 'column', gap: '24px', maxWidth: '960px' }}>

      {/* Encabezado */}
      <div style={{ display: 'flex', justifyContent: 'space-between', alignItems: 'flex-start', flexWrap: 'wrap', gap: 12 }}>
        <div>
          <h1 style={{ fontSize: 20, fontWeight: 600, color: 'var(--z-text-primary)', margin: 0 }}>
            Cargue de Pagos
          </h1>
          <p style={{ fontSize: 13, color: 'var(--z-text-muted)', margin: '4px 0 0' }}>
            Carga los pagos confirmados por cartera para alimentar los informes de recaudo.
          </p>
        </div>
      </div>

      {/* Cargando */}
      {esCargando && (
        <CargandoSpiral mensaje={mutValidar.isPending ? 'Validando archivo...' : 'Procesando cargue...'} />
      )}

      {/* ── Etapa: inicial ── */}
      {!esCargando && etapa === 'inicial' && (
        <div style={{
          background: 'var(--z-bg-card)', border: '0.5px solid var(--z-border)',
          borderRadius: '8px', padding: '24px', display: 'flex', flexDirection: 'column', gap: '20px',
        }}>

          {/* Paso 1 — Cartera */}
          <div style={{ display: 'flex', flexDirection: 'column', gap: '6px' }}>
            <p style={{ fontSize: '11px', fontWeight: 600, color: 'var(--z-text-muted)', textTransform: 'uppercase', letterSpacing: '0.04em' }}>
              Paso 1 — Selecciona la cartera
            </p>
            <div style={{ display: 'flex', alignItems: 'center', gap: '10px', flexWrap: 'wrap' }}>
              <select
                value={carteraID}
                onChange={e => setCarteraID(e.target.value ? Number(e.target.value) : '')}
                style={{
                  padding: '7px 10px', borderRadius: '6px', border: '0.5px solid var(--z-border)',
                  background: 'var(--z-bg-page)', color: carteraID ? 'var(--z-text-primary)' : 'var(--z-text-muted)',
                  fontSize: '13px', width: '280px', cursor: 'pointer', outline: 'none',
                }}
              >
                <option value="">— Seleccionar cartera —</option>
                {carteras.map(c => (
                  <option key={c.id} value={c.id}>{c.nombre}</option>
                ))}
              </select>
              <LeyendaUltimaActualizacion
                carteraID={carteraID}
                queryKey="pagos-ultima-actualizacion"
                queryFn={obtenerUltimaActualizacionPagos}
              />
            </div>
            <p style={{ fontSize: '12px', color: 'var(--z-text-muted)' }}>
              El líder de cada pago se asigna automáticamente según las asignaciones del sistema.
            </p>
          </div>

          {/* Paso 2 — Plantilla */}
          <div style={{ display: 'flex', flexDirection: 'column', gap: '6px' }}>
            <p style={{ fontSize: '11px', fontWeight: 600, color: 'var(--z-text-muted)', textTransform: 'uppercase', letterSpacing: '0.04em' }}>
              Paso 2 — Descarga la plantilla
            </p>
            <button
              onClick={async () => {
                setGenerandoPlantilla(true);
                await generarPlantillaPagos();
                setGenerandoPlantilla(false);
              }}
              disabled={generandoPlantilla}
              style={{
                padding: '8px 16px', borderRadius: '6px', border: '0.5px solid var(--z-border)',
                background: generandoPlantilla ? 'var(--z-border)' : 'var(--z-bg-page)',
                color: generandoPlantilla ? 'var(--z-text-muted)' : 'var(--z-text-primary)',
                fontSize: '13px', fontWeight: 600, width: 'fit-content',
                cursor: generandoPlantilla ? 'not-allowed' : 'pointer',
              }}
            >
              {generandoPlantilla ? 'Generando...' : 'Generar Plantilla'}
            </button>
            <p style={{ fontSize: '12px', color: 'var(--z-text-muted)' }}>
              Columnas requeridas: <strong>Cédula</strong>, <strong>Nombre</strong>, <strong>Valor</strong>,{' '}
              <strong>Fecha</strong>, <strong>Honorarios</strong>, <strong>UsuarioControl</strong>.
            </p>
          </div>

          {/* Paso 3 — Subir archivo */}
          <div style={{ display: 'flex', flexDirection: 'column', gap: '6px' }}>
            <p style={{ fontSize: '11px', fontWeight: 600, color: 'var(--z-text-muted)', textTransform: 'uppercase', letterSpacing: '0.04em' }}>
              Paso 3 — Sube el archivo completado
            </p>
            <div
              onClick={() => !!carteraID && inputRef.current?.click()}
              style={{
                border: `1.5px dashed ${archivoNombre ? 'var(--z-brand)' : 'var(--z-border)'}`,
                borderRadius: '8px', padding: '24px', textAlign: 'center',
                cursor: carteraID ? 'pointer' : 'not-allowed',
                background: archivoNombre ? '#eff6ff' : 'transparent',
                opacity: carteraID ? 1 : 0.5,
                transition: 'border-color 0.15s, background 0.15s',
              }}
            >
              <p style={{ fontSize: '13px', color: archivoNombre ? 'var(--z-brand)' : 'var(--z-text-muted)', marginBottom: '4px' }}>
                {archivoNombre || 'Haz clic para seleccionar un archivo .csv o .xlsx'}
              </p>
              {archivoNombre
                ? <p style={{ fontSize: '11px', color: 'var(--z-text-muted)' }}>{archivoKB.toFixed(1)} KB</p>
                : <p style={{ fontSize: '11px', color: 'var(--z-text-muted)' }}>CSV o XLSX — máx. 10 MB</p>
              }
            </div>
            <input
              ref={inputRef}
              type="file"
              accept=".csv,.xlsx"
              disabled={!carteraID}
              onChange={handleSeleccionarArchivo}
              style={{ display: 'none' }}
            />
            {archivoNombre && (
              <button
                onClick={() => { setArchivoNombre(''); setArchivoKB(0); if (inputRef.current) inputRef.current.value = ''; }}
                style={{ background: 'none', border: 'none', fontSize: '11px', color: 'var(--z-text-muted)', cursor: 'pointer', padding: 0, width: 'fit-content' }}
              >
                × Quitar archivo
              </button>
            )}
            {!carteraID && (
              <p style={{ fontSize: '12px', color: 'var(--z-text-muted)' }}>
                Selecciona primero la cartera para habilitar la carga.
              </p>
            )}
          </div>

          {errorMensaje && (
            <div style={{
              background: '#fef2f2', border: '0.5px solid #fca5a5',
              borderRadius: '6px', padding: '10px 14px',
              fontSize: '13px', color: '#dc2626',
            }}>
              {errorMensaje}
            </div>
          )}

          <div>
            <button
              onClick={() => mutValidar.mutate()}
              disabled={!puedeValidar}
              style={{
                padding: '9px 20px', borderRadius: '6px', border: 'none',
                background: puedeValidar ? 'var(--z-brand)' : 'var(--z-border)',
                color: puedeValidar ? '#fff' : 'var(--z-text-muted)',
                fontSize: '13px', fontWeight: 600,
                cursor: puedeValidar ? 'pointer' : 'not-allowed',
              }}
            >
              Validar archivo
            </button>
          </div>
        </div>
      )}

      {/* ── Etapa: preview ── */}
      {!esCargando && etapa === 'preview' && preview && (
        <div style={{ display: 'flex', flexDirection: 'column', gap: '16px' }}>

          {/* Chips de conteo (clickeables — toggle del detalle) */}
          <div style={{ display: 'flex', gap: '12px', flexWrap: 'wrap' }}>
            <ChipConteo
              numero={preview.cantidadValidas}
              label="pagos válidos"
              bg="#f0fdf4" border="#86efac" colorNum="#16a34a" colorLabel="#15803d"
              onClick={preview.cantidadValidas > 0 ? () => setVerValidos(v => !v) : undefined}
              abierto={verValidos}
            />
            {preview.cantidadDuplicados > 0 && (
              <ChipConteo
                numero={preview.cantidadDuplicados}
                label="ya cargados — se omitirán"
                bg="#fffbeb" border="#fcd34d" colorNum="#b45309" colorLabel="#92400e"
                onClick={() => setVerDuplicados(v => !v)}
                abierto={verDuplicados}
              />
            )}
            {preview.cantidadErrores > 0 && (
              <ChipConteo
                numero={preview.cantidadErrores}
                label="con error — no se cargarán"
                bg="#fef2f2" border="#fca5a5" colorNum="#dc2626" colorLabel="#b91c1c"
                onClick={() => setVerErrores(v => !v)}
                abierto={verErrores}
              />
            )}
          </div>

          {/* Panel de duplicados — TODOS los ya cargados (click en el chip ámbar) */}
          {verDuplicados && <PanelDuplicados filas={preview.duplicados} />}

          {/* Panel de errores — TODOS los errores (click en el chip rojo) */}
          {verErrores && <PanelErrores filas={preview.errores} />}

          {/* Tabla de válidos — primeros 10 (click en el chip verde) */}
          {verValidos && (
            <TablaPreview filas={preview.preview} totalValidas={preview.cantidadValidas} />
          )}

          {/* Aviso */}
          {preview.cantidadValidas > 0 && (
            <div style={{
              background: '#eff6ff', border: '0.5px solid #93c5fd',
              borderRadius: '8px', padding: '12px 16px',
              fontSize: '13px', color: '#1e40af',
            }}>
              <strong>Atención:</strong> los {preview.cantidadValidas} pagos válidos se insertarán
              directamente en la tabla <strong>Pago</strong>. Esta acción no se puede deshacer.
              {preview.cantidadDuplicados > 0 &&
                ` Los ${preview.cantidadDuplicados} pagos ya cargados se omitirán automáticamente.`}
            </div>
          )}

          {/* Acciones */}
          <div style={{ display: 'flex', gap: '10px', alignItems: 'center' }}>
            <button
              onClick={() => mutConfirmar.mutate()}
              disabled={preview.cantidadValidas === 0}
              style={{
                padding: '9px 20px', borderRadius: '6px', border: 'none',
                background: preview.cantidadValidas > 0 ? 'var(--z-brand)' : 'var(--z-border)',
                color: preview.cantidadValidas > 0 ? '#fff' : 'var(--z-text-muted)',
                fontSize: '13px', fontWeight: 600,
                cursor: preview.cantidadValidas > 0 ? 'pointer' : 'not-allowed',
              }}
            >
              Confirmar {preview.cantidadValidas} pago{preview.cantidadValidas !== 1 ? 's' : ''}
            </button>
            <button
              onClick={handleNuevaCarga}
              style={{
                padding: '9px 16px', borderRadius: '6px',
                border: '0.5px solid var(--z-border)', background: 'var(--z-bg-page)',
                color: 'var(--z-text-muted)', fontSize: '13px', cursor: 'pointer',
              }}
            >
              Cancelar
            </button>
            {preview.cantidadValidas === 0 && (
              <p style={{ fontSize: '13px', color: '#dc2626', margin: 0 }}>
                {preview.cantidadErrores === 0 && preview.cantidadDuplicados > 0
                  ? 'No hay pagos nuevos: todos los del archivo ya estaban cargados.'
                  : 'No hay pagos válidos. Corrige los errores y vuelve a subir el archivo.'}
              </p>
            )}
          </div>

          {errorMensaje && (
            <div style={{
              background: '#fef2f2', border: '0.5px solid #fca5a5',
              borderRadius: '6px', padding: '10px 14px',
              fontSize: '13px', color: '#dc2626',
            }}>
              {errorMensaje}
            </div>
          )}
        </div>
      )}

      {/* ── Etapa: resultado ── */}
      {!esCargando && etapa === 'resultado' && (
        <div style={{
          background: '#f0fdf4', border: '0.5px solid #86efac',
          borderRadius: '8px', padding: '24px',
          display: 'flex', flexDirection: 'column', gap: '16px',
        }}>
          <p style={{ fontSize: '15px', fontWeight: 600, color: '#15803d', margin: 0 }}>
            Cargue completado
          </p>
          <div style={{ display: 'flex', gap: '12px', flexWrap: 'wrap' }}>
            <div style={{
              background: '#dcfce7', border: '0.5px solid #86efac',
              borderRadius: '8px', padding: '12px 18px',
              display: 'flex', alignItems: 'center', gap: '8px',
            }}>
              <span style={{ fontSize: '22px', fontWeight: 700, color: '#15803d' }}>
                {insertados.toLocaleString('es-CO')}
              </span>
              <span style={{ fontSize: '13px', color: '#166534' }}>pagos insertados</span>
            </div>
          </div>
          <button
            onClick={handleNuevaCarga}
            style={{
              padding: '8px 16px', borderRadius: '6px',
              border: '0.5px solid #86efac', background: '#fff',
              color: '#15803d', fontSize: '13px', fontWeight: 600,
              cursor: 'pointer', width: 'fit-content',
            }}
          >
            Nueva carga
          </button>
        </div>
      )}

    </div>
  );
}
