import { useState } from 'react';
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query';
import { CargandoSpiral } from '../../components/CargandoSpiral';
import { TH, TD, btnPrimario, btnSecundario } from './historico-virtualidad/compartidos';
import { listarMensajesAutorizados, crearMensajeAutorizado, actualizarMensajeAutorizado, cambiarActivoMensajeAutorizado } from '../../api/mensajesAutorizadosApi';
import type { MensajeAutorizado, GuardarMensajeAutorizado } from '../../types/mensajesAutorizados';
import { FormularioMensaje } from './mensajes-autorizados/FormularioMensaje';

function mensajeDeError(err: unknown, porDefecto: string): string {
  const e = err as { response?: { data?: { mensaje?: string } } };
  return e.response?.data?.mensaje ?? porDefecto;
}

export function MensajesAutorizadosPage() {
  const qc = useQueryClient();
  const [editando, setEditando] = useState<MensajeAutorizado | null>(null);
  const [nuevo, setNuevo]       = useState(false);
  const [error, setError]       = useState<string | null>(null);

  const { data: mensajes = [], isLoading } = useQuery({
    queryKey: ['mensajes-autorizados'],
    queryFn:  () => listarMensajesAutorizados(false),
  });

  const invalidar = () => qc.invalidateQueries({ queryKey: ['mensajes-autorizados'] });

  const crear = useMutation({
    mutationFn: (req: GuardarMensajeAutorizado) => crearMensajeAutorizado(req),
    onSuccess: () => { setNuevo(false); setError(null); invalidar(); },
    onError:   e => setError(mensajeDeError(e, 'No se pudo crear el mensaje.')),
  });
  const actualizar = useMutation({
    mutationFn: ({ id, req }: { id: number; req: GuardarMensajeAutorizado }) => actualizarMensajeAutorizado(id, req),
    onSuccess: () => { setEditando(null); setError(null); invalidar(); },
    onError:   e => setError(mensajeDeError(e, 'No se pudo guardar el mensaje.')),
  });
  const cambiarActivo = useMutation({
    mutationFn: ({ id, activo }: { id: number; activo: boolean }) => cambiarActivoMensajeAutorizado(id, activo),
    onSuccess: invalidar,
  });

  const abrirNuevo  = () => { setEditando(null); setNuevo(true); setError(null); };
  const abrirEditar = (m: MensajeAutorizado) => { setNuevo(false); setEditando(m); setError(null); };
  const cerrar      = () => { setNuevo(false); setEditando(null); setError(null); };

  return (
    <div style={{ display: 'flex', flexDirection: 'column', gap: 24, maxWidth: 1100 }}>
      <div style={{ display: 'flex', justifyContent: 'space-between', alignItems: 'flex-start', flexWrap: 'wrap', gap: 12 }}>
        <div>
          <h1 style={{ fontSize: 20, fontWeight: 600, color: 'var(--z-text-primary)', margin: 0 }}>Mensajes Autorizados</h1>
          <p style={{ fontSize: 13, color: 'var(--z-text-muted)', margin: '4px 0 0' }}>
            Catálogo cerrado de textos SMS. Es lo único que un líder puede elegir en el Creador de Virtualidad; no se
            borran, se desactivan.
          </p>
        </div>
        {!nuevo && !editando && (
          <button onClick={abrirNuevo} style={btnPrimario(true)}>+ Nuevo mensaje</button>
        )}
      </div>

      {(nuevo || editando) && (
        <div style={{ background: 'var(--z-bg-card)', border: '1px solid var(--z-border)', borderRadius: 10, padding: 24 }}>
          <p style={{ margin: '0 0 14px', fontSize: 11, fontWeight: 600, color: 'var(--z-text-muted)', textTransform: 'uppercase', letterSpacing: '0.04em' }}>
            {editando ? `Editar: ${editando.titulo}` : 'Nuevo mensaje'}
          </p>
          {/* key: al cambiar de mensaje se remonta el formulario con el estado inicial nuevo. */}
          <FormularioMensaje
            key={editando?.id ?? 'nuevo'}
            inicial={editando}
            guardando={crear.isPending || actualizar.isPending}
            error={error}
            onCancelar={cerrar}
            onGuardar={req => editando ? actualizar.mutate({ id: editando.id, req }) : crear.mutate(req)}
          />
        </div>
      )}

      <div style={{ background: 'var(--z-bg-card)', border: '1px solid var(--z-border)', borderRadius: 10, overflow: 'hidden' }}>
        {isLoading ? <CargandoSpiral mensaje="Cargando catálogo…" /> : mensajes.length === 0 ? (
          <p style={{ margin: 0, padding: 32, textAlign: 'center', fontSize: 13, color: 'var(--z-text-muted)' }}>
            Aún no hay mensajes. Crea el primero.
          </p>
        ) : (
          <div style={{ overflowX: 'auto' }}>
            <table style={{ width: '100%', borderCollapse: 'collapse' }}>
              <thead>
                <tr>
                  <th style={TH}>Título</th>
                  <th style={TH}>Texto</th>
                  <th style={{ ...TH, textAlign: 'right' }}>Fijo</th>
                  <th style={TH}>Estado</th>
                  <th style={TH}>Actualizado</th>
                  <th style={TH}></th>
                </tr>
              </thead>
              <tbody>
                {mensajes.map(m => (
                  <tr key={m.id} style={{ opacity: m.activo ? 1 : 0.55 }}>
                    <td style={{ ...TD, fontWeight: 600 }}>{m.titulo}</td>
                    <td style={{ ...TD, whiteSpace: 'pre-wrap', fontFamily: 'ui-monospace, Menlo, monospace', fontSize: 12, maxWidth: 480 }}>{m.texto}</td>
                    <td style={{ ...TD, textAlign: 'right', fontVariantNumeric: 'tabular-nums' }}>{m.longitudBase}</td>
                    <td style={TD}>
                      <span style={{ fontSize: 11, fontWeight: 600, color: m.activo ? '#16a34a' : 'var(--z-text-muted)' }}>
                        {m.activo ? 'Activo' : 'Inactivo'}
                      </span>
                    </td>
                    <td style={{ ...TD, fontSize: 12, color: 'var(--z-text-muted)' }}>
                      {new Date(m.fechaActualizacion).toLocaleDateString('es-CO')} · {m.usuarioActualizacion}
                    </td>
                    <td style={{ ...TD, textAlign: 'right' }}>
                      <div style={{ display: 'flex', gap: 6, justifyContent: 'flex-end' }}>
                        <button onClick={() => abrirEditar(m)} style={{ ...btnSecundario, padding: '4px 10px', fontSize: 12 }}>Editar</button>
                        <button onClick={() => cambiarActivo.mutate({ id: m.id, activo: !m.activo })} style={{ ...btnSecundario, padding: '4px 10px', fontSize: 12 }}>
                          {m.activo ? 'Desactivar' : 'Activar'}
                        </button>
                      </div>
                    </td>
                  </tr>
                ))}
              </tbody>
            </table>
          </div>
        )}
      </div>
    </div>
  );
}
