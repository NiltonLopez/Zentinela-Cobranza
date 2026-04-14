import { useRef, useState } from 'react';
import { useMutation, useQuery } from '@tanstack/react-query';
import { generarPlantillaReporte, validarArchivoReporte, confirmarCargueReporte } from '../../api/reportesApi';
import { obtenerCarteras } from '../../api/carterasApi';
import type { PreviewReporteDto, EstadoFilaReporte } from '../../types/reportes';
import { CargandoSpiral } from '../../components/CargandoSpiral';

// ── Helpers ──────────────────────────────────────────────────────────────────

type Etapa = 'inicial' | 'preview' | 'resultado';

function colorEstado(estado: EstadoFilaReporte): string {
  switch (estado) {
    case 'Ok':               return 'var(--z-text-primary)';
    case 'NuevoTitular':     return '#1d4ed8'; // azul — se creará
    default:                 return '#dc2626'; // rojo — error
  }
}

function etiquetaEstado(estado: EstadoFilaReporte, razon: string | null): string {
  if (estado === 'Ok')           return 'Listo';
  if (estado === 'NuevoTitular') return razon ?? 'Titular nuevo — se creará';
  return razon ?? estado;
}

// ── Componente ───────────────────────────────────────────────────────────────

export function CargueReportesPage() {
  const inputArchivoRef = useRef<HTMLInputElement>(null);

  const [etapa,          setEtapa]          = useState<Etapa>('inicial');
  const [carteraID,      setCarteraID]      = useState<number>(0);
  const [archivoNombre,  setArchivoNombre]  = useState('');
  const [preview,        setPreview]        = useState<PreviewReporteDto | null>(null);
  const [mensajeFinal,   setMensajeFinal]   = useState('');
  const [errorMensaje,   setErrorMensaje]   = useState('');

  const { data: carteras = [] } = useQuery({
    queryKey: ['carteras'],
    queryFn:  obtenerCarteras,
  });

  const mutValidar = useMutation({
    mutationFn: ({ archivo, cid }: { archivo: File; cid: number }) =>
      validarArchivoReporte(archivo, cid),
    onSuccess: (data) => {
      setPreview(data);
      setEtapa('preview');
    },
    onError: () => setErrorMensaje('No se pudo validar el archivo. Verifica el formato.'),
  });

  const mutConfirmar = useMutation({
    mutationFn: () => confirmarCargueReporte({ filasValidas: preview!.filasValidasRaw }),
    onSuccess: (data) => {
      setMensajeFinal(data.mensaje);
      setEtapa('resultado');
    },
    onError: () => setErrorMensaje('Ocurrió un error al confirmar el cargue.'),
  });

  const handleSeleccionarArchivo = (e: React.ChangeEvent<HTMLInputElement>) => {
    const archivo = e.target.files?.[0];
    if (!archivo) return;
    if (carteraID === 0) {
      setErrorMensaje('Selecciona una cartera antes de subir el archivo.');
      return;
    }
    setArchivoNombre(archivo.name);
    setErrorMensaje('');
    mutValidar.mutate({ archivo, cid: carteraID });
  };

  const handleNuevaCarga = () => {
    setEtapa('inicial');
    setPreview(null);
    setArchivoNombre('');
    setMensajeFinal('');
    setErrorMensaje('');
    if (inputArchivoRef.current) inputArchivoRef.current.value = '';
  };

  const esCargando = mutValidar.isPending || mutConfirmar.isPending;

  return (
    <div style={{ display: 'flex', flexDirection: 'column', gap: '24px', maxWidth: '900px' }}>

      {/* Encabezado */}
      <div>
        <h1 style={{ fontSize: '16px', fontWeight: 600, color: 'var(--z-text-primary)', marginBottom: '4px' }}>
          Cargue de Reportes
        </h1>
        <p style={{ fontSize: '13px', color: 'var(--z-text-muted)' }}>
          Actualiza <strong>UltimoEstadoTitular</strong> (UPSERT) y enriquece <strong>HistoricoEstadoTitular</strong> (append) desde plantilla Excel.
          Compatible con carga manual y con SSIS.
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

          {/* Selector de cartera */}
          <div style={{ display: 'flex', flexDirection: 'column', gap: '6px' }}>
            <p style={{ fontSize: '11px', fontWeight: 600, color: 'var(--z-text-muted)', textTransform: 'uppercase', letterSpacing: '0.04em' }}>
              Paso 1 — Selecciona la cartera
            </p>
            <select
              value={carteraID}
              onChange={(e) => setCarteraID(Number(e.target.value))}
              style={{
                padding: '7px 10px', borderRadius: '6px', border: '0.5px solid var(--z-border)',
                background: 'var(--z-bg-page)', color: 'var(--z-text-primary)',
                fontSize: '13px', width: '280px', cursor: 'pointer',
              }}
            >
              <option value={0}>— Seleccionar cartera —</option>
              {carteras.map(c => (
                <option key={c.id} value={c.id}>{c.nombreCartera}</option>
              ))}
            </select>
          </div>

          {/* Generar plantilla */}
          <div style={{ display: 'flex', flexDirection: 'column', gap: '6px' }}>
            <p style={{ fontSize: '11px', fontWeight: 600, color: 'var(--z-text-muted)', textTransform: 'uppercase', letterSpacing: '0.04em' }}>
              Paso 2 — Descarga la plantilla
            </p>
            <button
              onClick={() => generarPlantillaReporte()}
              style={{
                padding: '8px 16px', borderRadius: '6px', border: '0.5px solid var(--z-border)',
                background: 'var(--z-bg-page)', color: 'var(--z-text-primary)',
                fontSize: '13px', fontWeight: 600, cursor: 'pointer', width: 'fit-content',
              }}
            >
              Generar plantilla .xlsx
            </button>
            <p style={{ fontSize: '12px', color: 'var(--z-text-muted)' }}>
              Campos obligatorios: <strong>TitularID</strong>, <strong>AgenteID</strong>.
              Si el TitularID es nuevo, agrega <strong>Nombre</strong> para crearlo.
              La cartera se asigna automáticamente desde el selector.
            </p>
          </div>

          {/* Subir archivo */}
          <div style={{ display: 'flex', flexDirection: 'column', gap: '6px' }}>
            <p style={{ fontSize: '11px', fontWeight: 600, color: 'var(--z-text-muted)', textTransform: 'uppercase', letterSpacing: '0.04em' }}>
              Paso 3 — Sube el archivo completado
            </p>
            <label
              htmlFor="archivo-reporte"
              style={{
                display: 'inline-flex', alignItems: 'center', gap: '8px',
                padding: '8px 16px', borderRadius: '6px',
                border: '0.5px dashed var(--z-border)', background: 'var(--z-bg-page)',
                color: carteraID === 0 ? 'var(--z-text-muted)' : 'var(--z-text-primary)',
                fontSize: '13px', fontWeight: 500,
                cursor: carteraID === 0 ? 'not-allowed' : 'pointer', width: 'fit-content',
                opacity: carteraID === 0 ? 0.6 : 1,
              }}
            >
              {archivoNombre || 'Seleccionar archivo .xlsx'}
            </label>
            <input
              id="archivo-reporte"
              ref={inputArchivoRef}
              type="file"
              accept=".xlsx"
              disabled={carteraID === 0}
              onChange={handleSeleccionarArchivo}
              style={{ display: 'none' }}
            />
          </div>

          {errorMensaje && (
            <p style={{ fontSize: '13px', color: '#dc2626' }}>{errorMensaje}</p>
          )}
        </div>
      )}

      {/* ── Etapa: preview ── */}
      {!esCargando && etapa === 'preview' && preview && (
        <div style={{ display: 'flex', flexDirection: 'column', gap: '16px' }}>

          {/* Conteos */}
          <div style={{ display: 'flex', gap: '12px', flexWrap: 'wrap' }}>
            <div style={{
              display: 'flex', alignItems: 'center', gap: '8px',
              background: '#f0fdf4', border: '0.5px solid #86efac',
              borderRadius: '8px', padding: '12px 18px',
            }}>
              <span style={{ fontSize: '22px', fontWeight: 700, color: '#16a34a' }}>
                {preview.totalValidos}
              </span>
              <span style={{ fontSize: '13px', color: '#15803d' }}>registros válidos</span>
            </div>
            {preview.totalNuevos > 0 && (
              <div style={{
                display: 'flex', alignItems: 'center', gap: '8px',
                background: '#eff6ff', border: '0.5px solid #93c5fd',
                borderRadius: '8px', padding: '12px 18px',
              }}>
                <span style={{ fontSize: '22px', fontWeight: 700, color: '#1d4ed8' }}>
                  {preview.totalNuevos}
                </span>
                <span style={{ fontSize: '13px', color: '#1e40af' }}>titulares nuevos</span>
              </div>
            )}
            {preview.totalInvalidos > 0 && (
              <div style={{
                display: 'flex', alignItems: 'center', gap: '8px',
                background: '#fef2f2', border: '0.5px solid #fca5a5',
                borderRadius: '8px', padding: '12px 18px',
              }}>
                <span style={{ fontSize: '22px', fontWeight: 700, color: '#dc2626' }}>
                  {preview.totalInvalidos}
                </span>
                <span style={{ fontSize: '13px', color: '#b91c1c' }}>con error (se omitirán)</span>
              </div>
            )}
          </div>

          {/* Tabla de validación */}
          <div style={{
            background: 'var(--z-bg-card)', border: '0.5px solid var(--z-border)',
            borderRadius: '8px', overflow: 'hidden',
          }}>
            <p style={{
              padding: '12px 16px', fontSize: '12px', color: 'var(--z-text-muted)',
              borderBottom: '0.5px solid var(--z-border)', margin: 0,
            }}>
              Vista previa — primeros {preview.filas.length} registros del archivo
            </p>
            <div style={{ overflowX: 'auto' }}>
              <table style={{ width: '100%', borderCollapse: 'collapse' }}>
                <thead>
                  <tr style={{ background: 'var(--z-bg-page)' }}>
                    {['#', 'TitularID', 'Nombre', 'AgenteID', 'Estado'].map((col, i) => (
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
                  {preview.filas.map((fila, idx) => (
                    <tr key={idx} style={{ borderBottom: '0.5px solid var(--z-border)' }}>
                      <td style={{ padding: '10px 14px', fontSize: '13px', color: 'var(--z-text-muted)' }}>
                        {idx + 1}
                      </td>
                      <td style={{ padding: '10px 14px', fontSize: '13px', color: 'var(--z-text-primary)', fontWeight: 500 }}>
                        {fila.titularID}
                      </td>
                      <td style={{ padding: '10px 14px', fontSize: '13px', color: 'var(--z-text-muted)', fontStyle: fila.nombre ? 'normal' : 'italic' }}>
                        {fila.nombre ?? '—'}
                      </td>
                      <td style={{ padding: '10px 14px', fontSize: '13px', color: 'var(--z-text-primary)' }}>
                        {fila.agenteID}
                      </td>
                      <td style={{ padding: '10px 14px', fontSize: '13px', color: colorEstado(fila.estado) }}>
                        {etiquetaEstado(fila.estado, fila.razon)}
                      </td>
                    </tr>
                  ))}
                </tbody>
              </table>
            </div>
          </div>

          {/* Aviso de comportamiento */}
          <div style={{
            background: '#eff6ff', border: '0.5px solid #93c5fd',
            borderRadius: '8px', padding: '12px 16px',
            fontSize: '13px', color: '#1e40af',
          }}>
            <strong>UltimoEstadoTitular:</strong> los {preview.totalValidos} registros válidos se actualizarán por UPSERT (si existen, se modifican; si no, se crean).
            {preview.totalNuevos > 0 && (
              <> <strong>{preview.totalNuevos}</strong> titular(es) nuevo(s) se crearán en la tabla <strong>Titular</strong>.</>
            )}
            <br />
            <strong>HistoricoEstadoTitular:</strong> se agregarán solo los registros cuya combinación TitularID+CarteraID+AgenteID no exista aún.
          </div>

          {/* Acciones */}
          <div style={{ display: 'flex', gap: '10px' }}>
            <button
              onClick={() => mutConfirmar.mutate()}
              disabled={preview.totalValidos === 0}
              style={{
                padding: '9px 20px', borderRadius: '6px',
                border: 'none',
                background: preview.totalValidos > 0 ? 'var(--z-brand)' : 'var(--z-border)',
                color: preview.totalValidos > 0 ? '#fff' : 'var(--z-text-muted)',
                fontSize: '13px', fontWeight: 600,
                cursor: preview.totalValidos > 0 ? 'pointer' : 'not-allowed',
              }}
            >
              Confirmar {preview.totalValidos} registro{preview.totalValidos !== 1 ? 's' : ''}
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
          </div>

          {errorMensaje && (
            <p style={{ fontSize: '13px', color: '#dc2626' }}>{errorMensaje}</p>
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
          <p style={{ fontSize: '15px', fontWeight: 600, color: '#15803d' }}>
            Cargue completado
          </p>
          <p style={{ fontSize: '13px', color: '#166534' }}>{mensajeFinal}</p>
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
