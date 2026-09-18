import { useEffect, useState } from 'react';
import { useMutation, useQuery } from '@tanstack/react-query';
import { ZDropdown } from '../../../components/ZDropdown';
import { CargandoSpiral } from '../../../components/CargandoSpiral';
import { TH, TD, btnPrimario, btnSecundario, cajaError } from '../../administracion/historico-virtualidad/compartidos';
import { obtenerAsesoresDelLider, obtenerAgentesActivos, guardarContactosAsesores, obtenerMensajesDisponibles, mensajeDeErrorBlob } from '../../../api/creadorVirtualidadApi';
import type { EstadoAsistente, Accion, FilaAsesor } from './estadoAsistente';
import { asesorValido, linkWaMe, linkMasLargo, CELULAR_REGEX } from './estadoAsistente';
import { C, inputStyle, etiquetaPaso, Aviso, BarraAcciones } from './compartidosSms';

interface Props {
  estado: EstadoAsistente;
  dispatch: React.Dispatch<Accion>;
}

export function PasoEnlaces({ estado, dispatch }: Props) {
  const [progreso, setProgreso] = useState(0);
  const [error, setError]       = useState<string | null>(null);
  const [agregarID, setAgregarID] = useState<number>(0);

  const { data: precargados, isLoading } = useQuery({
    queryKey: ['sms-asesores', estado.liderID],
    queryFn:  () => obtenerAsesoresDelLider(estado.liderID),
  });

  const { data: activos = [] } = useQuery({
    queryKey: ['sms-agentes-activos'],
    queryFn:  obtenerAgentesActivos,
    staleTime: 10 * 60 * 1000,
  });

  // Precarga una sola vez por líder: si el usuario ya editó filas, no se pisan.
  useEffect(() => {
    if (!precargados || estado.asesores.length > 0) return;
    dispatch({
      tipo: 'SET_ASESORES',
      asesores: precargados.map(a => {
        const numero = a.numeroCorporativo ?? '';
        const link   = a.linkWhatsapp ?? (CELULAR_REGEX.test(numero) ? linkWaMe(numero) : '');
        return {
          agenteID: a.agenteID, nombre: a.nombre, numeroCorporativo: numero, linkWhatsapp: link,
          precargado: a.precargado,
          linkEditadoManual: !!a.linkWhatsapp && a.linkWhatsapp !== linkWaMe(numero),
        };
      }),
    });
  }, [precargados, estado.asesores.length, dispatch]);

  const actualizar = (agenteID: number, cambio: Partial<FilaAsesor>) =>
    dispatch({ tipo: 'SET_ASESORES', asesores: estado.asesores.map(a => a.agenteID === agenteID ? { ...a, ...cambio } : a) });

  const cambiarNumero = (a: FilaAsesor, valor: string) => {
    const numero = valor.replace(/\D/g, '').slice(0, 10);
    const cambio: Partial<FilaAsesor> = { numeroCorporativo: numero };
    // Mientras el link no se haya editado a mano, se propone wa.me/57<numero> (18 caracteres).
    if (!a.linkEditadoManual) cambio.linkWhatsapp = CELULAR_REGEX.test(numero) ? linkWaMe(numero) : '';
    actualizar(a.agenteID, cambio);
  };

  const cambiarLink = (a: FilaAsesor, valor: string) =>
    actualizar(a.agenteID, { linkWhatsapp: valor.trim(), linkEditadoManual: valor.trim().length > 0 });

  const restaurarLink = (a: FilaAsesor) =>
    actualizar(a.agenteID, { linkWhatsapp: CELULAR_REGEX.test(a.numeroCorporativo) ? linkWaMe(a.numeroCorporativo) : '', linkEditadoManual: false });

  const eliminar = (agenteID: number) =>
    dispatch({ tipo: 'SET_ASESORES', asesores: estado.asesores.filter(a => a.agenteID !== agenteID) });

  const agregar = () => {
    const ag = activos.find(a => a.agenteID === agregarID);
    if (!ag || estado.asesores.some(a => a.agenteID === ag.agenteID)) return;
    const numero = ag.numeroCorporativo ?? '';
    dispatch({
      tipo: 'SET_ASESORES',
      asesores: [...estado.asesores, {
        agenteID: ag.agenteID, nombre: ag.nombre, numeroCorporativo: numero,
        linkWhatsapp: ag.linkWhatsapp ?? (CELULAR_REGEX.test(numero) ? linkWaMe(numero) : ''),
        precargado: false, linkEditadoManual: !!ag.linkWhatsapp && ag.linkWhatsapp !== linkWaMe(numero),
      }],
    });
    setAgregarID(0);
  };

  // Continuar = persistir contactos y pedir el catálogo anotado con el link más largo.
  const continuar = useMutation({
    mutationFn: async () => {
      const validos = estado.asesores.filter(asesorValido);
      await guardarContactosAsesores(validos.map(a => ({ agenteID: a.agenteID, numeroCorporativo: a.numeroCorporativo.trim(), linkWhatsapp: a.linkWhatsapp.trim() })));
      return obtenerMensajesDisponibles(
        { archivo: estado.archivo!, liderID: estado.liderID, carterasListaNegra: estado.carterasListaNegra, archivoListaNegra: estado.archivoListaNegra },
        estado.validacion!.huellaArchivo,
        validos.map(a => a.linkWhatsapp.trim()),
        setProgreso,
      );
    },
    onMutate:  () => { setError(null); setProgreso(0); },
    onSuccess: catalogo => { dispatch({ tipo: 'SET_CATALOGO', catalogo }); dispatch({ tipo: 'IR_A', paso: 3 }); },
    onError:   async e => {
      const status = (e as { response?: { status?: number } }).response?.status;
      const msg = await mensajeDeErrorBlob(e, 'No se pudo continuar.');
      if (status === 409) { dispatch({ tipo: 'SET_ARCHIVO', archivo: null }); }
      setError(msg);
    },
  });

  const validos   = estado.asesores.filter(asesorValido);
  const invalidos = estado.asesores.length - validos.length;
  const puede     = validos.length > 0 && invalidos === 0 && !continuar.isPending;
  const candidatos = activos.filter(a => !estado.asesores.some(x => x.agenteID === a.agenteID));

  if (isLoading) return <CargandoSpiral mensaje="Cargando asesores del líder…" />;

  return (
    <div style={{ display: 'flex', flexDirection: 'column', gap: 18 }}>
      <div>
        <p style={etiquetaPaso}>Enlaces a distribuir</p>
        <p style={{ margin: '4px 0 0', fontSize: 13, color: 'var(--z-text-muted)' }}>
          Una fila por asesor. Escribe el <strong>número corporativo</strong> y el sistema propone el enlace
          <code style={{ margin: '0 4px' }}>wa.me/57…</code>(18 caracteres). Si tienes un enlace más corto (wa.link), pégalo encima:
          cada carácter que ahorres aquí es un carácter más para el mensaje. Todo se guarda por asesor para la próxima vez.
        </p>
      </div>

      {estado.asesores.length === 0 && (
        <Aviso tipo="warning">Este líder no tiene asesores en Cartera_Agente. Agrega al menos uno desde la lista de agentes activos.</Aviso>
      )}

      <div style={{ overflowX: 'auto', border: '0.5px solid var(--z-border)', borderRadius: 8 }}>
        <table style={{ width: '100%', borderCollapse: 'collapse' }}>
          <thead>
            <tr>
              <th style={TH}>Asesor</th>
              <th style={{ ...TH, width: 170 }}>Número corporativo</th>
              <th style={{ ...TH, width: 300 }}>Link Wpp</th>
              <th style={{ ...TH, width: 60 }}></th>
            </tr>
          </thead>
          <tbody>
            {estado.asesores.map(a => {
              const numOk  = CELULAR_REGEX.test(a.numeroCorporativo);
              const linkOk = a.linkWhatsapp.trim().length > 0;
              return (
                <tr key={a.agenteID}>
                  <td style={TD}>
                    {a.nombre}
                    {!a.precargado && <span style={{ marginLeft: 8, fontSize: 10, color: C.sms, fontWeight: 600, textTransform: 'uppercase' }}>prestado</span>}
                  </td>
                  <td style={TD}>
                    <input value={a.numeroCorporativo} onChange={e => cambiarNumero(a, e.target.value)} placeholder="3001234567" inputMode="numeric"
                      style={{ ...inputStyle, borderColor: a.numeroCorporativo && !numOk ? C.danger : 'var(--z-border)' }} />
                  </td>
                  <td style={TD}>
                    <div style={{ display: 'flex', gap: 6, alignItems: 'center' }}>
                      <input value={a.linkWhatsapp} onChange={e => cambiarLink(a, e.target.value)} placeholder="wa.me/57… o wa.link/…"
                        style={{ ...inputStyle, borderColor: a.numeroCorporativo && !linkOk ? C.danger : 'var(--z-border)' }} />
                      {a.linkEditadoManual && numOk && (
                        <button onClick={() => restaurarLink(a)} title="Volver al wa.me generado"
                          style={{ background: 'none', border: 'none', cursor: 'pointer', color: 'var(--z-text-muted)', fontSize: 12 }}>↺</button>
                      )}
                    </div>
                    <span style={{ fontSize: 10, color: 'var(--z-text-muted)' }}>{a.linkWhatsapp.trim().length} caracteres</span>
                  </td>
                  <td style={{ ...TD, textAlign: 'center' }}>
                    <button onClick={() => eliminar(a.agenteID)} title="Quitar de esta estrategia"
                      style={{ background: 'none', border: 'none', cursor: 'pointer', color: C.danger, fontSize: 16 }}>×</button>
                  </td>
                </tr>
              );
            })}
          </tbody>
        </table>
      </div>

      <div style={{ display: 'flex', gap: 10, alignItems: 'center', flexWrap: 'wrap' }}>
        <span style={{ fontSize: 12, color: 'var(--z-text-muted)' }}>Agregar asesor:</span>
        <ZDropdown
          options={[{ value: 0, label: 'Selecciona un agente activo…' }, ...candidatos.map(a => ({ value: a.agenteID, label: a.nombre }))]}
          value={agregarID}
          onChange={setAgregarID}
          background="var(--z-bg-page)"
          style={{ minWidth: 280 }}
        />
        <button onClick={agregar} disabled={agregarID === 0} style={btnSecundario}>+ Añadir fila</button>
      </div>

      {invalidos > 0 && (
        <Aviso tipo="warning">{invalidos} fila{invalidos === 1 ? '' : 's'} sin número válido (10 dígitos, empieza en 3) o sin link. Complétalas o quítalas.</Aviso>
      )}
      {validos.length > 0 && (
        <span style={{ fontSize: 12, color: 'var(--z-text-muted)' }}>
          Enlace más largo: <strong>{linkMasLargo(validos)} caracteres</strong> — con ese se calcula si cada mensaje cabe en 160.
        </span>
      )}

      {continuar.isPending && <CargandoSpiral mensaje={`Evaluando mensajes${progreso > 0 && progreso < 100 ? ` — subiendo ${progreso}%` : '…'}`} />}
      {error && <div style={cajaError}>{error}</div>}

      <BarraAcciones>
        <button onClick={() => dispatch({ tipo: 'IR_A', paso: 1 })} style={btnSecundario}>← Atrás</button>
        <button onClick={() => dispatch({ tipo: 'REINICIAR' })} style={btnSecundario}>Cancelar</button>
        <button onClick={() => continuar.mutate()} disabled={!puede} style={btnPrimario(puede)}>Continuar →</button>
      </BarraAcciones>
    </div>
  );
}
