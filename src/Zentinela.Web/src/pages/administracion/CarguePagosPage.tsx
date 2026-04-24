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

function ChipConteo({ numero, label, bg, border, colorNum, colorLabel }: {
  numero: number; label: string;
  bg: string; border: string; colorNum: string; colorLabel: string;
}) {
  return (
    <div style={{
      display: 'flex', alignItems: 'center', gap: '8px',
      background: bg, border: `0.5px solid ${border}`,
      borderRadius: '8px', padding: '12px 18px',
    }}>
      <span style={{ fontSize: '22px', fontWeight: 700, color: colorNum }}>
        {numero.toLocaleString('es-CO')}
      </span>
      <span style={{ fontSize: '13px', color: colorLabel }}>{label}</span>
    </div>
  );
}

// ── Panel de errores ──────────────────────────────────────────────────────────

function PanelErrores({ filas, totalErrores }: { filas: PagoFila[]; totalErrores: number }) {
  if (filas.length === 0) return null;
  const hayMas = totalErrores > filas.length;
  return (
    <div style={{
      background: '#fef2f2', border: '1px solid #fca5a5',
      borderRadius: '8px', overflow: 'hidden',
    }}>
      <div style={{
        padding: '12px 16px', borderBottom: '1px solid #fca5a5',
        display: 'flex', alignItems: 'center', gap: '10px',
      }}>
        <span style={{
          display: 'inline-flex', alignItems: 'center', justifyContent: 'center',
          width: 24, height: 24, borderRadius: '50%',
          background: '#dc2626', color: '#fff', fontSize: '13px', fontWeight: 700, flexShrink: 0,
        }}>
          {totalErrores}
        </span>
        <p style={{ margin: 0, fontSize: '13px', fontWeight: 600, color: '#b91c1c' }}>
          {totalErrores === 1
            ? '1 registro con error — no se cargará'
            : `${totalErrores} registros con error — no se cargarán`}
          {hayMas && (
            <span style={{ fontWeight: 400, marginLeft: 6 }}>
              (mostrando primeros {filas.length})
            </span>
          )}
        </p>
      </div>
      <div style={{ overflowX: 'auto' }}>
        <table style={{ width: '100%', borderCollapse: 'collapse' }}>
          <thead>
            <tr style={{ background: '#fef2f2' }}>
              {['#', 'Cédula', 'Nombre', 'Motivo del error'].map((col, i) => (
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
              <tr key={idx} style={{ borderBottom: '0.5px solid #fecaca' }}>
                <td style={{ padding: '9px 14px', fontSize: '13px', color: '#dc2626', fontWeight: 500 }}>
                  {idx + 1}
                </td>
                <td style={{ padding: '9px 14px', fontSize: '13px', color: '#b91c1c', fontWeight: 600 }}>
                  {fila.cedulaTitular || '—'}
                </td>
                <td style={{ padding: '9px 14px', fontSize: '13px', color: '#b91c1c' }}>
                  {fila.nombreCliente || '—'}
                </td>
                <td style={{ padding: '9px 14px', fontSize: '13px', color: '#dc2626', fontWeight: 500 }}>
                  {fila.errorMensaje ?? 'Error de validación'}
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

function TablaPreview({ filas }: { filas: PagoFila[] }) {
  const validas = filas.filter(f => f.esValida);
  if (validas.length === 0) return null;
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
        Muestra — primeros {validas.length} registros válidos
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
            {validas.map((fila, idx) => (
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
                  {fila.usuarioControl}
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
    if (inputRef.current) inputRef.current.value = '';
  };

  const esCargando   = mutValidar.isPending || mutConfirmar.isPending;
  const puedeValidar = !!carteraID && !!archivoNombre;

  const filasConError = preview?.preview.filter(f => !f.esValida) ?? [];

  return (
    <div style={{ display: 'flex', flexDirection: 'column', gap: '24px', maxWidth: '960px' }}>

      {/* Encabezado */}
      <div>
        <h1 style={{ fontSize: '16px', fontWeight: 600, color: 'var(--z-text-primary)', marginBottom: '4px' }}>
          Cargue de Pagos
        </h1>
        <p style={{ fontSize: '13px', color: 'var(--z-text-muted)' }}>
          Carga los pagos confirmados por cartera para alimentar los informes de recaudo.
        </p>
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

          {/* Chips de conteo */}
          <div style={{ display: 'flex', gap: '12px', flexWrap: 'wrap' }}>
            <ChipConteo
              numero={preview.cantidadValidas}
              label="pagos válidos"
              bg="#f0fdf4" border="#86efac" colorNum="#16a34a" colorLabel="#15803d"
            />
            {preview.cantidadErrores > 0 && (
              <ChipConteo
                numero={preview.cantidadErrores}
                label="con error — no se cargarán"
                bg="#fef2f2" border="#fca5a5" colorNum="#dc2626" colorLabel="#b91c1c"
              />
            )}
          </div>

          {/* Panel de errores */}
          <PanelErrores filas={filasConError} totalErrores={preview.cantidadErrores} />

          {/* Tabla de válidos */}
          <TablaPreview filas={preview.preview} />

          {/* Aviso */}
          <div style={{
            background: '#fffbeb', border: '0.5px solid #fcd34d',
            borderRadius: '8px', padding: '12px 16px',
            fontSize: '13px', color: '#92400e',
          }}>
            <strong>Atención:</strong> los {preview.cantidadValidas} pagos válidos se insertarán
            directamente en la tabla <strong>Pago</strong>. Esta acción no se puede deshacer.
          </div>

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
                No hay pagos válidos. Corrige los errores y vuelve a subir el archivo.
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
