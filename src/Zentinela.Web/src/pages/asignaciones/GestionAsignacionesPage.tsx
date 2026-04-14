import { useRef, useState } from 'react';
import { useMutation, useQuery } from '@tanstack/react-query';
import { obtenerOpcionesCalificador } from '../../api/calificadorApi';
import { generarPlantilla, validarArchivo, confirmarCargue } from '../../api/asignacionesApi';
import type { PreviewAsignacionDto, EstadoValidacion } from '../../types/asignaciones';
import { CargandoSpiral } from '../../components/CargandoSpiral';

// ── Helpers de estilo ────────────────────────────────────────────────────────

type Etapa = 'inicial' | 'preview' | 'resultado';

function colorEstado(estado: EstadoValidacion): string {
  if (estado === 'NoExiste')   return '#dc2626';
  if (estado === 'YaAsignado') return '#d97706';
  return 'var(--z-text-primary)';
}

function etiquetaEstado(estado: EstadoValidacion, razon: string | null): string {
  if (estado === 'Ok') return 'Listo';
  return razon ?? estado;
}

// ── Componente principal ─────────────────────────────────────────────────────

export function GestionAsignacionesPage() {
  const inputArchivoRef = useRef<HTMLInputElement>(null);

  const [etapa,         setEtapa]         = useState<Etapa>('inicial');
  const [seleccion,     setSeleccion]     = useState('');        // "carteraID_liderID"
  const [archivoNombre, setArchivoNombre] = useState('');
  const [preview,       setPreview]       = useState<PreviewAsignacionDto | null>(null);
  const [mensajeFinal,  setMensajeFinal]  = useState('');
  const [errorMensaje,  setErrorMensaje]  = useState('');

  // Dropdown opciones Cartera+Líder (reutiliza calificador)
  const { data: opciones = [], isLoading: cargandoOpciones } = useQuery({
    queryKey: ['asignaciones-opciones'],
    queryFn:  obtenerOpcionesCalificador,
    staleTime: 10 * 60 * 1000,
  });

  // Mutación: validar Excel
  const mutValidar = useMutation({
    mutationFn: async (archivo: File) => {
      const [carteraIDStr, liderIDStr] = seleccion.split('_');
      return validarArchivo(archivo, Number(carteraIDStr), Number(liderIDStr));
    },
    onSuccess: (data) => {
      setPreview(data);
      setEtapa('preview');
    },
    onError: () => setErrorMensaje('No se pudo validar el archivo. Verifica el formato.'),
  });

  // Mutación: confirmar cargue
  const mutConfirmar = useMutation({
    mutationFn: () => {
      const [carteraIDStr, liderIDStr] = seleccion.split('_');
      return confirmarCargue({
        carteraID:  Number(carteraIDStr),
        liderID:    Number(liderIDStr),
        titularIDs: preview!.titularIDsValidos,
      });
    },
    onSuccess: (data) => {
      setMensajeFinal(data.mensaje);
      setEtapa('resultado');
    },
    onError: () => setErrorMensaje('Ocurrió un error al confirmar el cargue.'),
  });

  const handleSeleccionarArchivo = (e: React.ChangeEvent<HTMLInputElement>) => {
    const archivo = e.target.files?.[0];
    if (!archivo) return;
    setArchivoNombre(archivo.name);
    setErrorMensaje('');
    if (!seleccion) {
      setErrorMensaje('Selecciona primero la cartera y el líder.');
      return;
    }
    mutValidar.mutate(archivo);
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
          Gestión de Asignaciones
        </h1>
        <p style={{ fontSize: '13px', color: 'var(--z-text-muted)' }}>
          Cargue masivo de titulares a una cartera mediante plantilla Excel.
        </p>
      </div>

      {/* ── Etapa: cargando ── */}
      {esCargando && (
        <CargandoSpiral mensaje={mutValidar.isPending ? 'Validando archivo...' : 'Confirmando cargue...'} />
      )}

      {/* ── Etapa: inicial ── */}
      {!esCargando && etapa === 'inicial' && (
        <div style={{
          background: 'var(--z-bg-card)', border: '0.5px solid var(--z-border)',
          borderRadius: '8px', padding: '24px', display: 'flex', flexDirection: 'column', gap: '20px',
        }}>

          {/* Selector Cartera+Líder */}
          <div style={{ display: 'flex', flexDirection: 'column', gap: '6px' }}>
            <label style={{ fontSize: '11px', fontWeight: 600, color: 'var(--z-text-muted)', textTransform: 'uppercase', letterSpacing: '0.04em' }}>
              Cartera + Líder
            </label>
            {cargandoOpciones ? (
              <p style={{ fontSize: '13px', color: 'var(--z-text-muted)' }}>Cargando opciones...</p>
            ) : (
              <select
                value={seleccion}
                onChange={e => setSeleccion(e.target.value)}
                style={{
                  padding: '8px 10px', fontSize: '13px', borderRadius: '6px',
                  border: '0.5px solid var(--z-border)', background: 'var(--z-bg-page)',
                  color: 'var(--z-text-primary)', outline: 'none', maxWidth: '400px',
                }}
              >
                <option value="">— Selecciona cartera y líder —</option>
                {opciones.map(o => (
                  <option key={`${o.carteraID}_${o.liderID}`} value={`${o.carteraID}_${o.liderID}`}>
                    {o.etiqueta}
                  </option>
                ))}
              </select>
            )}
          </div>

          {/* Botón generar plantilla */}
          <div style={{ display: 'flex', flexDirection: 'column', gap: '6px' }}>
            <p style={{ fontSize: '11px', fontWeight: 600, color: 'var(--z-text-muted)', textTransform: 'uppercase', letterSpacing: '0.04em' }}>
              Paso 1 — Descarga la plantilla
            </p>
            <button
              onClick={() => generarPlantilla()}
              style={{
                padding: '8px 16px', borderRadius: '6px', border: '0.5px solid var(--z-border)',
                background: 'var(--z-bg-page)', color: 'var(--z-text-primary)',
                fontSize: '13px', fontWeight: 600, cursor: 'pointer', width: 'fit-content',
              }}
            >
              Generar plantilla .xlsx
            </button>
            <p style={{ fontSize: '12px', color: 'var(--z-text-muted)' }}>
              Llena la columna <strong>NumeroDocumento</strong> con las cédulas de los titulares a asignar.
            </p>
          </div>

          {/* Área de carga */}
          <div style={{ display: 'flex', flexDirection: 'column', gap: '6px' }}>
            <p style={{ fontSize: '11px', fontWeight: 600, color: 'var(--z-text-muted)', textTransform: 'uppercase', letterSpacing: '0.04em' }}>
              Paso 2 — Sube el archivo completado
            </p>
            <label
              htmlFor="archivo-asignacion"
              style={{
                display: 'inline-flex', alignItems: 'center', gap: '8px',
                padding: '8px 16px', borderRadius: '6px',
                border: '0.5px dashed var(--z-border)', background: 'var(--z-bg-page)',
                color: seleccion ? 'var(--z-text-primary)' : 'var(--z-text-muted)',
                fontSize: '13px', fontWeight: 500,
                cursor: seleccion ? 'pointer' : 'not-allowed',
                width: 'fit-content',
              }}
            >
              {archivoNombre || 'Seleccionar archivo .xlsx'}
            </label>
            <input
              id="archivo-asignacion"
              ref={inputArchivoRef}
              type="file"
              accept=".xlsx"
              disabled={!seleccion}
              onChange={handleSeleccionarArchivo}
              style={{ display: 'none' }}
            />
            {!seleccion && (
              <p style={{ fontSize: '12px', color: 'var(--z-text-muted)' }}>
                Selecciona primero la cartera y el líder para habilitar la carga.
              </p>
            )}
          </div>

          {/* Error */}
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
              <span style={{ fontSize: '13px', color: '#15803d' }}>listos para asignar</span>
            </div>
            {preview.totalInvalidos > 0 && (
              <div style={{
                display: 'flex', alignItems: 'center', gap: '8px',
                background: '#fef2f2', border: '0.5px solid #fca5a5',
                borderRadius: '8px', padding: '12px 18px',
              }}>
                <span style={{ fontSize: '22px', fontWeight: 700, color: '#dc2626' }}>
                  {preview.totalInvalidos}
                </span>
                <span style={{ fontSize: '13px', color: '#b91c1c' }}>con error (no se asignarán)</span>
              </div>
            )}
          </div>

          {/* Tabla de validación (primeros 10) */}
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
            <table style={{ width: '100%', borderCollapse: 'collapse' }}>
              <thead>
                <tr style={{ background: 'var(--z-bg-page)' }}>
                  {['#', 'NumeroDocumento', 'Nombre', 'Estado'].map((col, i) => (
                    <th key={i} style={{
                      padding: '9px 14px', textAlign: 'left',
                      fontSize: '11px', fontWeight: 600, color: 'var(--z-text-muted)',
                      borderBottom: '0.5px solid var(--z-border)',
                      letterSpacing: '0.04em', textTransform: 'uppercase',
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
                      {fila.numeroDocumento}
                    </td>
                    <td style={{ padding: '10px 14px', fontSize: '13px', color: 'var(--z-text-muted)' }}>
                      {fila.nombre ?? '—'}
                    </td>
                    <td style={{ padding: '10px 14px', fontSize: '13px', color: colorEstado(fila.estado) }}>
                      {etiquetaEstado(fila.estado, fila.razon)}
                    </td>
                  </tr>
                ))}
              </tbody>
            </table>
          </div>

          {/* Aviso de reemplazo */}
          <div style={{
            background: '#fffbeb', border: '0.5px solid #fcd34d',
            borderRadius: '8px', padding: '12px 16px',
            fontSize: '13px', color: '#92400e',
          }}>
            <strong>Atención:</strong> confirmar reemplazará <em>todas</em> las asignaciones actuales de esta cartera+líder con los {preview.totalValidos} titulares válidos del archivo.
          </div>

          {/* Acciones */}
          <div style={{ display: 'flex', gap: '10px' }}>
            <button
              onClick={() => mutConfirmar.mutate()}
              disabled={preview.totalValidos === 0}
              style={{
                padding: '9px 20px', borderRadius: '6px',
                border: 'none', background: preview.totalValidos > 0 ? 'var(--z-brand)' : 'var(--z-border)',
                color: preview.totalValidos > 0 ? '#fff' : 'var(--z-text-muted)',
                fontSize: '13px', fontWeight: 600,
                cursor: preview.totalValidos > 0 ? 'pointer' : 'not-allowed',
              }}
            >
              Reemplazar con {preview.totalValidos} asignacion{preview.totalValidos !== 1 ? 'es' : ''}
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
