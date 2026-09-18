import { useState } from 'react';
import { btnPrimario, btnSecundario, TH, TD } from '../../administracion/historico-virtualidad/compartidos';
import type { EstadoAsistente, Accion } from './estadoAsistente';
import type { MensajeDisponible, EstadoMensajeDisponible } from '../../../types/creadorVirtualidad';
import { LIMITE_SMS } from '../../../types/mensajesAutorizados';
import { C, etiquetaPaso, Aviso, BarraAcciones } from './compartidosSms';

interface Props {
  estado: EstadoAsistente;
  dispatch: React.Dispatch<Accion>;
}

// Clave = valor del enum tal como lo serializa la API (texto). Un estado desconocido cae a
// "No disponible" en vez de reventar el render.
const ESTILO_ESTADO: Record<EstadoMensajeDisponible, { color: string; bg: string; label: string }> = {
  Disponible:    { color: C.success, bg: '#f0fdf4', label: 'Disponible' },
  Advertencia:   { color: C.warning, bg: '#fffbeb', label: 'Con advertencia' },
  Deshabilitado: { color: C.danger,  bg: '#fef2f2', label: 'No disponible' },
};

export function PasoMensaje({ estado, dispatch }: Props) {
  const [verExcedidos, setVerExcedidos] = useState<number | null>(null);
  const cat = estado.catalogo;
  if (!cat) return null;

  const seleccionado = cat.mensajes.find(m => m.id === estado.mensajeID) ?? null;
  const puede = !!seleccionado && seleccionado.estado !== 'Deshabilitado';

  const Tarjeta = ({ m }: { m: MensajeDisponible }) => {
    const e = ESTILO_ESTADO[m.estado] ?? ESTILO_ESTADO.Deshabilitado;
    const deshabilitado = m.estado === 'Deshabilitado';
    const activo = estado.mensajeID === m.id;
    return (
      <div
        onClick={() => !deshabilitado && dispatch({ tipo: 'SET_MENSAJE', mensajeID: activo ? null : m.id })}
        style={{
          border: `1.5px solid ${activo ? C.sms : 'var(--z-border)'}`, borderRadius: 10, padding: '14px 16px',
          background: activo ? '#faf5ff' : 'var(--z-bg-card)', cursor: deshabilitado ? 'not-allowed' : 'pointer',
          opacity: deshabilitado ? 0.6 : 1, display: 'flex', flexDirection: 'column', gap: 8,
        }}>
        <div style={{ display: 'flex', justifyContent: 'space-between', alignItems: 'center', gap: 10, flexWrap: 'wrap' }}>
          <strong style={{ fontSize: 14 }}>{m.titulo}</strong>
          <span style={{ fontSize: 11, fontWeight: 600, color: e.color, background: e.bg, border: `1px solid ${e.color}40`, borderRadius: 999, padding: '2px 10px' }}>
            {e.label}
          </span>
        </div>
        <p style={{ margin: 0, fontSize: 13, whiteSpace: 'pre-wrap', fontFamily: 'ui-monospace, SFMono-Regular, Menlo, monospace', color: 'var(--z-text-primary)' }}>
          {m.texto}
        </p>
        <div style={{ display: 'flex', gap: 14, fontSize: 11, color: 'var(--z-text-muted)', flexWrap: 'wrap' }}>
          <span>Texto fijo: <strong>{m.longitudBase}</strong></span>
          <span>Máximo con tus datos: <strong style={{ color: m.longitudMaxima > LIMITE_SMS ? C.danger : 'inherit' }}>{m.longitudMaxima}</strong> / {LIMITE_SMS}</span>
          {m.totalExcedidos > 0 && (
            <button onClick={ev => { ev.stopPropagation(); setVerExcedidos(verExcedidos === m.id ? null : m.id); }}
              style={{ background: 'none', border: 'none', color: e.color, cursor: 'pointer', fontSize: 11, fontWeight: 600, padding: 0 }}>
              {verExcedidos === m.id ? 'ocultar' : 'ver'} los {m.totalExcedidos} que se pasan ▾
            </button>
          )}
        </div>
        {m.motivo && <span style={{ fontSize: 12, color: e.color, fontWeight: 500 }}>⚠ {m.motivo}</span>}
        {verExcedidos === m.id && m.excedidos.length > 0 && (
          <div onClick={ev => ev.stopPropagation()} style={{ maxHeight: 220, overflow: 'auto', border: '0.5px solid var(--z-border)', borderRadius: 6 }}>
            <table style={{ width: '100%', borderCollapse: 'collapse' }}>
              <thead><tr><th style={TH}>Cédula</th><th style={TH}>Nombre</th><th style={TH}>Caracteres</th></tr></thead>
              <tbody>{m.excedidos.map(t => (
                <tr key={t.cedula}><td style={TD}>{t.cedula}</td><td style={TD}>{t.nombre}</td><td style={{ ...TD, color: C.danger }}>{t.longitud}</td></tr>
              ))}</tbody>
            </table>
            {m.totalExcedidos > m.excedidos.length && (
              <p style={{ margin: 0, padding: '6px 10px', fontSize: 11, color: 'var(--z-text-muted)' }}>Mostrando {m.excedidos.length} de {m.totalExcedidos}.</p>
            )}
          </div>
        )}
      </div>
    );
  };

  return (
    <div style={{ display: 'flex', flexDirection: 'column', gap: 18 }}>
      <div>
        <p style={etiquetaPaso}>Seleccionar mensaje autorizado</p>
        <p style={{ margin: '4px 0 0', fontSize: 13, color: 'var(--z-text-muted)' }}>
          Estos son los mensajes autorizados a enviar, si desea ingresar un mensaje acá, comuníquelo a un director o administrador.
        </p>
      </div>

      <Aviso tipo="info">
        El límite es de <strong>{LIMITE_SMS} caracteres</strong> contando el nombre de cada titular, el celular del asesor (10)
        y el enlace más largo de tus asesores (<strong>{cat.linkMasLargo}</strong> caracteres). Titulares: <strong>{cat.totalTitulares.toLocaleString('es-CO')}</strong>.
      </Aviso>

      {cat.mensajes.length === 0 && <Aviso tipo="danger">No hay mensajes autorizados activos. Pide a un director o administrador que cargue uno.</Aviso>}

      <div style={{ display: 'grid', gridTemplateColumns: 'repeat(auto-fill, minmax(360px, 1fr))', gap: 12 }}>
        {cat.mensajes.map(m => <Tarjeta key={m.id} m={m} />)}
      </div>

      <BarraAcciones>
        <button onClick={() => dispatch({ tipo: 'IR_A', paso: 2 })} style={btnSecundario}>← Atrás</button>
        <button onClick={() => dispatch({ tipo: 'REINICIAR' })} style={btnSecundario}>Cancelar</button>
        <button onClick={() => dispatch({ tipo: 'IR_A', paso: 4 })} disabled={!puede} style={btnPrimario(puede)}>Continuar →</button>
      </BarraAcciones>
    </div>
  );
}
