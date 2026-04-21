import { useRef, useState } from 'react';
import { useQuery, useMutation } from '@tanstack/react-query';
import {
  listarCarterasPagos,
  validarCsvPagos,
  confirmarCarguePagos,
  generarPlantillaPagos,
} from '../../api/pagosApi';
import type { ValidarPagosResponse } from '../../types/pagos';

type Paso = 'carga' | 'preview' | 'exito';

export function CarguePagosPage() {
  const [carteraID, setCarteraID]   = useState<number | ''>('');
  const [archivo, setArchivo]       = useState<File | null>(null);
  const [preview, setPreview]       = useState<ValidarPagosResponse | null>(null);
  const [paso, setPaso]             = useState<Paso>('carga');
  const [feedback, setFeedback]     = useState<string | null>(null);
  const [errorMsg, setErrorMsg]     = useState<string | null>(null);
  const inputRef = useRef<HTMLInputElement>(null);

  const [generandoPlantilla, setGenerandoPlantilla] = useState(false);

  const { data: carteras = [] } = useQuery({
    queryKey: ['pagos-carteras'],
    queryFn: listarCarterasPagos,
  });

  const mutValidar = useMutation({
    mutationFn: () => validarCsvPagos(archivo!, Number(carteraID)),
    onSuccess: (data) => {
      setPreview(data);
      setErrorMsg(null);
      setPaso('preview');
    },
    onError: (err: unknown) => {
      const msg = (err as { response?: { data?: { mensaje?: string } } })
        ?.response?.data?.mensaje ?? 'Error al validar el archivo.';
      setErrorMsg(msg);
    },
  });

  const mutConfirmar = useMutation({
    mutationFn: () =>
      confirmarCarguePagos(Number(carteraID), preview!.todasLasValidas),
    onSuccess: (data) => {
      setFeedback(data.mensaje);
      setPaso('exito');
    },
    onError: (err: unknown) => {
      const msg = (err as { response?: { data?: { mensaje?: string } } })
        ?.response?.data?.mensaje ?? 'Error al confirmar el cargue.';
      setErrorMsg(msg);
    },
  });

  function reiniciar() {
    setCarteraID('');
    setArchivo(null);
    setPreview(null);
    setErrorMsg(null);
    setFeedback(null);
    setPaso('carga');
    if (inputRef.current) inputRef.current.value = '';
  }

  const puedeValidar = !!carteraID && !!archivo;

  return (
    <div style={{ maxWidth: '860px' }}>
      {/* Encabezado */}
      <div style={{ marginBottom: '24px' }}>
        <h2 style={{ fontSize: '18px', fontWeight: 600, color: 'var(--z-text-primary)', marginBottom: '4px' }}>
          Cargue de Pagos
        </h2>
        <p style={{ fontSize: '13px', color: 'var(--z-text-muted)' }}>
          Carga los pagos confirmados por cartera para alimentar los informes de recaudo.
        </p>
      </div>

      {/* ── Paso: Carga ───────────────────────────────────────────────────────── */}
      {paso === 'carga' && (
        <div style={{
          background: 'var(--z-bg-card)',
          border: '0.5px solid var(--z-border)',
          borderRadius: '12px',
          padding: '28px 32px',
        }}>
          {/* Selector de cartera */}
          <div style={{ marginBottom: '20px', maxWidth: '320px' }}>
            <label style={{ fontSize: '12px', fontWeight: 600, color: 'var(--z-text-muted)', display: 'block', marginBottom: '6px' }}>
              Cartera
            </label>
            <select
              value={carteraID}
              onChange={(e) => setCarteraID(e.target.value ? Number(e.target.value) : '')}
              style={{
                width: '100%', padding: '8px 10px',
                fontSize: '13px', border: '1px solid var(--z-border)',
                borderRadius: '6px', background: 'var(--z-bg-card)',
                color: carteraID ? 'var(--z-text-primary)' : 'var(--z-text-muted)',
                outline: 'none', cursor: 'pointer',
              }}
            >
              <option value="">Selecciona una cartera...</option>
              {carteras.map((c) => (
                <option key={c.id} value={c.id}>{c.nombre}</option>
              ))}
            </select>
            <p style={{ fontSize: '11px', color: 'var(--z-text-muted)', marginTop: '4px' }}>
              El líder de cada pago se asigna automáticamente según las asignaciones del sistema.
            </p>
          </div>

          {/* Plantilla */}
          <div style={{ marginBottom: '20px' }}>
            <label style={{ fontSize: '12px', fontWeight: 600, color: 'var(--z-text-muted)', display: 'block', marginBottom: '6px' }}>
              Plantilla
            </label>
            <button
              onClick={async () => {
                setGenerandoPlantilla(true);
                await generarPlantillaPagos();
                setGenerandoPlantilla(false);
              }}
              disabled={generandoPlantilla}
              style={{
                padding: '8px 16px', borderRadius: '6px', border: '1px solid var(--z-border)',
                background: generandoPlantilla ? 'var(--z-border)' : 'var(--z-bg-page)',
                color: generandoPlantilla ? 'var(--z-text-muted)' : 'var(--z-text-primary)',
                fontSize: '13px', fontWeight: 600,
                cursor: generandoPlantilla ? 'not-allowed' : 'pointer',
              }}
            >
              {generandoPlantilla ? 'Generando...' : 'Generar Plantilla'}
            </button>
            <p style={{ fontSize: '11px', color: 'var(--z-text-muted)', marginTop: '4px' }}>
              Columnas requeridas: Cédula, Nombre, Valor, Fecha, Honorarios, UsuarioControl.
            </p>
          </div>

          {/* Zona de carga de archivo */}
          <div style={{ marginBottom: '20px' }}>
            <label style={{ fontSize: '12px', fontWeight: 600, color: 'var(--z-text-muted)', display: 'block', marginBottom: '6px' }}>
              Archivo CSV o XLSX
            </label>
            <div
              onClick={() => inputRef.current?.click()}
              style={{
                border: `1.5px dashed ${archivo ? 'var(--z-brand)' : 'var(--z-border)'}`,
                borderRadius: '8px',
                padding: '24px',
                textAlign: 'center',
                cursor: 'pointer',
                background: archivo ? '#EFF6FF' : 'transparent',
                transition: 'border-color 0.15s, background 0.15s',
              }}
            >
              <p style={{ fontSize: '13px', color: archivo ? 'var(--z-brand)' : 'var(--z-text-muted)', marginBottom: '4px' }}>
                {archivo ? archivo.name : 'Haz clic para seleccionar un archivo .csv o .xlsx'}
              </p>
              {archivo && (
                <p style={{ fontSize: '11px', color: 'var(--z-text-muted)' }}>
                  {(archivo.size / 1024).toFixed(1)} KB
                </p>
              )}
              {!archivo && (
                <p style={{ fontSize: '11px', color: 'var(--z-text-muted)' }}>
                  Columnas requeridas: Cedula, Nombre, Valor, Fecha (YYYY-MM-DD), Honorarios, UsuarioControl
                </p>
              )}
            </div>
            <input
              ref={inputRef}
              type="file"
              accept=".csv,.xlsx"
              style={{ display: 'none' }}
              onChange={(e) => setArchivo(e.target.files?.[0] ?? null)}
            />
            {archivo && (
              <button
                onClick={() => { setArchivo(null); if (inputRef.current) inputRef.current.value = ''; }}
                style={{
                  marginTop: '6px', background: 'none', border: 'none',
                  fontSize: '11px', color: 'var(--z-text-muted)', cursor: 'pointer',
                  padding: 0,
                }}
              >
                × Quitar archivo
              </button>
            )}
          </div>

          {errorMsg && (
            <div style={{
              padding: '8px 12px', borderRadius: '6px', fontSize: '12px',
              background: '#fff5f5', border: '0.5px solid #fecaca',
              color: '#dc2626', marginBottom: '16px',
            }}>
              {errorMsg}
            </div>
          )}

          <button
            onClick={() => mutValidar.mutate()}
            disabled={!puedeValidar || mutValidar.isPending}
            style={{
              padding: '9px 20px', borderRadius: '6px',
              border: 'none', background: 'var(--z-brand)',
              color: 'white', fontSize: '13px', fontWeight: 600,
              cursor: (!puedeValidar || mutValidar.isPending) ? 'not-allowed' : 'pointer',
              opacity: (!puedeValidar || mutValidar.isPending) ? 0.5 : 1,
            }}
          >
            {mutValidar.isPending ? 'Validando...' : 'Validar archivo'}
          </button>
        </div>
      )}

      {/* ── Paso: Preview ─────────────────────────────────────────────────────── */}
      {paso === 'preview' && preview && (
        <div>
          <div style={{
            background: 'var(--z-bg-card)',
            border: '0.5px solid var(--z-border)',
            borderRadius: '12px',
            padding: '20px 28px',
            marginBottom: '16px',
            display: 'flex',
            gap: '32px',
            flexWrap: 'wrap',
            alignItems: 'center',
          }}>
            <div>
              <p style={{ fontSize: '11px', color: 'var(--z-text-muted)', marginBottom: '2px' }}>Total filas</p>
              <p style={{ fontSize: '22px', fontWeight: 700, color: 'var(--z-text-primary)' }}>{preview.totalFilas}</p>
            </div>
            <div>
              <p style={{ fontSize: '11px', color: 'var(--z-text-muted)', marginBottom: '2px' }}>Válidas</p>
              <p style={{ fontSize: '22px', fontWeight: 700, color: '#16a34a' }}>{preview.cantidadValidas}</p>
            </div>
            {preview.cantidadErrores > 0 && (
              <div>
                <p style={{ fontSize: '11px', color: 'var(--z-text-muted)', marginBottom: '2px' }}>Con error</p>
                <p style={{ fontSize: '22px', fontWeight: 700, color: '#dc2626' }}>{preview.cantidadErrores}</p>
              </div>
            )}
            <div style={{ marginLeft: 'auto', display: 'flex', gap: '10px' }}>
              <button
                onClick={reiniciar}
                style={{
                  padding: '8px 16px', borderRadius: '6px',
                  border: '1px solid var(--z-border)', background: 'transparent',
                  color: 'var(--z-text-muted)', fontSize: '13px', cursor: 'pointer',
                }}
              >
                Cancelar
              </button>
              <button
                onClick={() => mutConfirmar.mutate()}
                disabled={preview.cantidadValidas === 0 || mutConfirmar.isPending}
                style={{
                  padding: '8px 20px', borderRadius: '6px',
                  border: 'none', background: 'var(--z-brand)',
                  color: 'white', fontSize: '13px', fontWeight: 600,
                  cursor: (preview.cantidadValidas === 0 || mutConfirmar.isPending) ? 'not-allowed' : 'pointer',
                  opacity: (preview.cantidadValidas === 0 || mutConfirmar.isPending) ? 0.5 : 1,
                }}
              >
                {mutConfirmar.isPending ? 'Cargando...' : `Confirmar ${preview.cantidadValidas} pagos`}
              </button>
            </div>
          </div>

          {errorMsg && (
            <div style={{
              padding: '8px 12px', borderRadius: '6px', fontSize: '12px',
              background: '#fff5f5', border: '0.5px solid #fecaca',
              color: '#dc2626', marginBottom: '12px',
            }}>
              {errorMsg}
            </div>
          )}

          <div style={{
            background: 'var(--z-bg-card)',
            border: '0.5px solid var(--z-border)',
            borderRadius: '12px',
            overflow: 'hidden',
          }}>
            <p style={{ fontSize: '12px', color: 'var(--z-text-muted)', padding: '12px 16px 0', marginBottom: '-4px' }}>
              Mostrando las primeras {preview.preview.length} filas del archivo
            </p>
            <div style={{ overflowX: 'auto' }}>
              <table style={{ width: '100%', borderCollapse: 'collapse', fontSize: '12px' }}>
                <thead>
                  <tr style={{ borderBottom: '0.5px solid var(--z-border)' }}>
                    {['Cédula', 'Nombre', 'Valor Pagado', 'Fecha', 'Honorarios', 'Usuario Control', 'Estado'].map((h) => (
                      <th key={h} style={{
                        padding: '10px 16px', textAlign: 'left',
                        fontSize: '11px', fontWeight: 600,
                        color: 'var(--z-text-muted)', whiteSpace: 'nowrap',
                      }}>{h}</th>
                    ))}
                  </tr>
                </thead>
                <tbody>
                  {preview.preview.map((fila, i) => (
                    <tr
                      key={i}
                      style={{
                        borderBottom: '0.5px solid var(--z-border)',
                        background: !fila.esValida ? '#fff5f5' : 'transparent',
                      }}
                    >
                      <td style={{ padding: '9px 16px', color: 'var(--z-text-primary)' }}>{fila.cedulaTitular}</td>
                      <td style={{ padding: '9px 16px', color: 'var(--z-text-primary)' }}>{fila.nombreCliente}</td>
                      <td style={{ padding: '9px 16px', color: 'var(--z-text-primary)' }}>
                        ${fila.valorPagado.toLocaleString('es-CO')}
                      </td>
                      <td style={{ padding: '9px 16px', color: 'var(--z-text-primary)', whiteSpace: 'nowrap' }}>{fila.fechaPago}</td>
                      <td style={{ padding: '9px 16px', color: 'var(--z-text-primary)' }}>
                        ${fila.honorarios.toLocaleString('es-CO')}
                      </td>
                      <td style={{ padding: '9px 16px', color: 'var(--z-text-primary)' }}>
                        {fila.usuarioControl}
                      </td>
                      <td style={{ padding: '9px 16px' }}>
                        {fila.esValida ? (
                          <span style={{
                            fontSize: '11px', fontWeight: 500, padding: '2px 8px',
                            borderRadius: '4px', background: '#f0fdf4', color: '#16a34a',
                          }}>OK</span>
                        ) : (
                          <div style={{ display: 'flex', flexDirection: 'column', gap: 2 }}>
                            <span style={{
                              fontSize: '11px', fontWeight: 500, padding: '2px 8px',
                              borderRadius: '4px', background: '#fff5f5', color: '#dc2626',
                              width: 'fit-content',
                            }}>Error</span>
                            {fila.errorMensaje && (
                              <span style={{ fontSize: '11px', color: '#dc2626' }}>
                                {fila.errorMensaje}
                              </span>
                            )}
                          </div>
                        )}
                      </td>
                    </tr>
                  ))}
                </tbody>
              </table>
            </div>
          </div>
        </div>
      )}

      {/* ── Paso: Éxito ───────────────────────────────────────────────────────── */}
      {paso === 'exito' && (
        <div style={{
          background: 'var(--z-bg-card)',
          border: '0.5px solid #bbf7d0',
          borderRadius: '12px',
          padding: '40px 32px',
          textAlign: 'center',
        }}>
          <p style={{ fontSize: '16px', fontWeight: 600, color: '#16a34a', marginBottom: '8px' }}>
            Cargue completado
          </p>
          <p style={{ fontSize: '13px', color: 'var(--z-text-muted)', marginBottom: '24px' }}>
            {feedback}
          </p>
          <button
            onClick={reiniciar}
            style={{
              padding: '9px 24px', borderRadius: '6px',
              border: 'none', background: 'var(--z-brand)',
              color: 'white', fontSize: '13px', fontWeight: 600,
              cursor: 'pointer',
            }}
          >
            Cargar otro archivo
          </button>
        </div>
      )}
    </div>
  );
}
