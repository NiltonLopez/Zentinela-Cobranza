import { useState } from 'react';
import { useMutation } from '@tanstack/react-query';
import { CargandoSpiral } from '../../../components/CargandoSpiral';
import { btnPrimario, btnSecundario, cajaError, TH, TD, ChipConteo } from '../../administracion/historico-virtualidad/compartidos';
import { generarEstrategiaSms, mensajeDeErrorBlob } from '../../../api/creadorVirtualidadApi';
import type { EstadoAsistente, Accion } from './estadoAsistente';
import { asesorValido } from './estadoAsistente';
import { C, etiquetaPaso, Aviso, BarraAcciones } from './compartidosSms';

interface Props {
  estado: EstadoAsistente;
  dispatch: React.Dispatch<Accion>;
}

export function PasoGenerar({ estado, dispatch }: Props) {
  const [progreso, setProgreso] = useState(0);
  const [error, setError]       = useState<string | null>(null);

  const cat = estado.catalogo;
  const mensaje = cat?.mensajes.find(m => m.id === estado.mensajeID) ?? null;
  const asesores = estado.asesores.filter(asesorValido);

  // El hook va antes de cualquier return condicional (reglas de hooks).
  const generar = useMutation({
    mutationFn: () => generarEstrategiaSms(
      { archivo: estado.archivo!, liderID: estado.liderID, carterasListaNegra: estado.carterasListaNegra, archivoListaNegra: estado.archivoListaNegra },
      estado.validacion!.huellaArchivo,
      mensaje!.id,
      asesores.map(a => ({ agenteID: a.agenteID, numeroCorporativo: a.numeroCorporativo.trim(), linkWhatsapp: a.linkWhatsapp.trim() })),
      setProgreso,
    ),
    onMutate:  () => { setError(null); setProgreso(0); },
    onSuccess: r => dispatch({ tipo: 'SET_GENERACION', generacion: r }),
    onError:   async e => {
      const status = (e as { response?: { status?: number } }).response?.status;
      setError(await mensajeDeErrorBlob(e, 'No se pudo generar el archivo.'));
      if (status === 409) dispatch({ tipo: 'SET_ARCHIVO', archivo: null });
    },
  });

  if (!cat || !mensaje || !estado.validacion) return null;
  const quedan = cat.totalTitulares - mensaje.totalExcedidos;
  const g = estado.generacion;

  if (g) {
    return (
      <div style={{ display: 'flex', flexDirection: 'column', gap: 18 }}>
        <Aviso tipo="success">
          <strong>Archivo generado y descargado:</strong> {g.nombreArchivo} — {g.totalGenerados.toLocaleString('es-CO')} SMS.
          Entrégalo al área de virtualidad. Registro #{g.estrategiaID}.
        </Aviso>
        {g.excluidosLongitud > 0 && (
          <span style={{ fontSize: 13, color: 'var(--z-text-muted)' }}>Se excluyeron {g.excluidosLongitud} titulares porque su mensaje superaba 160 caracteres.</span>
        )}
        <BarraAcciones>
          <button onClick={() => dispatch({ tipo: 'REINICIAR' })} style={btnPrimario(true)}>Nueva estrategia</button>
        </BarraAcciones>
      </div>
    );
  }

  return (
    <div style={{ display: 'flex', flexDirection: 'column', gap: 18 }}>
      <div>
        <p style={etiquetaPaso}>Resumen y generación</p>
        <p style={{ margin: '4px 0 0', fontSize: 13, color: 'var(--z-text-muted)' }}>
          Cada titular recibe un asesor al azar con reparto parejo. El archivo trae las 5 columnas de tu plantilla más
          <strong> Celular Asesor, Link Asesor y Mensaje</strong> ya armado.
        </p>
      </div>

      <div style={{ display: 'flex', gap: 10, flexWrap: 'wrap' }}>
        <ChipConteo numero={cat.totalTitulares} label="titulares válidos" bg="var(--z-bg-page)" border="var(--z-border)" colorNum="var(--z-text-primary)" colorLabel="var(--z-text-muted)" />
        <ChipConteo numero={mensaje.totalExcedidos} label="se borrarán (superan 160)" bg={mensaje.totalExcedidos > 0 ? '#fef2f2' : 'var(--z-bg-page)'} border={mensaje.totalExcedidos > 0 ? C.danger + '55' : 'var(--z-border)'} colorNum={mensaje.totalExcedidos > 0 ? C.danger : 'var(--z-text-muted)'} colorLabel={mensaje.totalExcedidos > 0 ? C.danger : 'var(--z-text-muted)'} />
        <ChipConteo numero={quedan} label="SMS en el archivo" bg="#f0fdf4" border={C.success + '55'} colorNum={C.success} colorLabel={C.success} />
        <ChipConteo numero={asesores.length} label="asesores" bg="#f5f3ff" border={C.sms + '55'} colorNum={C.sms} colorLabel={C.sms} />
      </div>

      <div style={{ border: '0.5px solid var(--z-border)', borderRadius: 8, padding: '12px 14px', display: 'flex', flexDirection: 'column', gap: 6 }}>
        <span style={{ fontSize: 11, fontWeight: 600, color: 'var(--z-text-muted)', textTransform: 'uppercase', letterSpacing: '0.04em' }}>Mensaje: {mensaje.titulo}</span>
        <p style={{ margin: 0, fontSize: 13, whiteSpace: 'pre-wrap', fontFamily: 'ui-monospace, SFMono-Regular, Menlo, monospace' }}>{mensaje.texto}</p>
      </div>

      {mensaje.totalExcedidos > 0 && (
        <div style={{ display: 'flex', flexDirection: 'column', gap: 8 }}>
          <Aviso tipo="warning">
            <strong>{mensaje.totalExcedidos} titular{mensaje.totalExcedidos === 1 ? '' : 'es'}</strong> no entrarán al archivo porque su mensaje
            supera 160 caracteres. Quedarán <strong>{quedan.toLocaleString('es-CO')}</strong>.
          </Aviso>
          <div style={{ maxHeight: 220, overflow: 'auto', border: '0.5px solid var(--z-border)', borderRadius: 6 }}>
            <table style={{ width: '100%', borderCollapse: 'collapse' }}>
              <thead><tr><th style={TH}>Cédula</th><th style={TH}>Nombre</th><th style={TH}>Caracteres</th></tr></thead>
              <tbody>{mensaje.excedidos.map(t => (
                <tr key={t.cedula}><td style={TD}>{t.cedula}</td><td style={TD}>{t.nombre}</td><td style={{ ...TD, color: C.danger }}>{t.longitud}</td></tr>
              ))}</tbody>
            </table>
          </div>
        </div>
      )}

      {generar.isPending && <CargandoSpiral mensaje={`Generando archivo${progreso > 0 && progreso < 100 ? ` — subiendo ${progreso}%` : '…'}`} />}
      {error && <div style={cajaError}>{error}</div>}

      <BarraAcciones>
        <button onClick={() => dispatch({ tipo: 'IR_A', paso: 3 })} style={btnSecundario}>← Atrás</button>
        <button onClick={() => dispatch({ tipo: 'REINICIAR' })} style={btnSecundario}>Cancelar</button>
        <button onClick={() => generar.mutate()} disabled={generar.isPending || quedan <= 0} style={{ ...btnPrimario(!generar.isPending && quedan > 0), background: !generar.isPending && quedan > 0 ? C.success : undefined }}>
          {generar.isPending ? 'Generando…' : '↓ Generar y descargar XLSX'}
        </button>
      </BarraAcciones>
    </div>
  );
}
