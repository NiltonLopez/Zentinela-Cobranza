import { useMemo, useState } from 'react';
import { useMutation } from '@tanstack/react-query';
import { btnPrimario, btnSecundario, cajaError } from '../historico-virtualidad/compartidos';
import { validarMensajeAutorizado } from '../../../api/mensajesAutorizadosApi';
import type { MensajeAutorizado, GuardarMensajeAutorizado, CaracterRechazado } from '../../../types/mensajesAutorizados';
import { PLACEHOLDERS, LIMITE_SMS, LONGITUD_CELULAR, LONGITUD_LINK_WAME } from '../../../types/mensajesAutorizados';

interface Props {
  inicial: MensajeAutorizado | null;
  guardando: boolean;
  error: string | null;
  onGuardar: (req: GuardarMensajeAutorizado) => void;
  onCancelar: () => void;
}

const inputStyle: React.CSSProperties = {
  width: '100%', padding: '8px 10px', fontSize: 13, border: '1px solid var(--z-border)',
  borderRadius: 6, background: 'var(--z-bg-card)', color: 'var(--z-text-primary)', outline: 'none', fontFamily: 'inherit',
};

const NOMBRE_REFERENCIA  = 22; // nombre completo típico, para el contador orientativo
const CARTERA_REFERENCIA = 16; // "Pelicano Propia"
const EMPRESA_REFERENCIA = 16; // "Banco Bellavida"

/**
 * Regla local, espejo de CatalogoCreadorVirtualidad.CaracteresNoGsm7 (la definitiva es la
 * del servidor; esta solo pinta en vivo). Rechaza tildes, ñ y símbolos de la extensión
 * GSM-7 salvo las llaves de los tres placeholders.
 */
function caracteresSospechosos(texto: string): CaracterRechazado[] {
  const enPlaceholder = new Array<boolean>(texto.length).fill(false);
  for (const ph of PLACEHOLDERS) {
    let i = texto.indexOf(ph);
    while (i >= 0) { for (let k = i; k < i + ph.length; k++) enPlaceholder[k] = true; i = texto.indexOf(ph, i + 1); }
  }
  const rechazados: CaracterRechazado[] = [];
  for (let i = 0; i < texto.length; i++) {
    if (enPlaceholder[i]) continue;
    const c = texto[i];
    const basico = /[A-Za-z0-9 @$!"#%&'()*+,\-./:;<=>?_¡¿§£¥\n\r]/.test(c);
    if (!basico) rechazados.push({ caracter: c, posicion: i + 1 });
  }
  return rechazados;
}

export function FormularioMensaje({ inicial, guardando, error, onGuardar, onCancelar }: Props) {
  const [titulo, setTitulo] = useState(inicial?.titulo ?? '');
  const [texto, setTexto]   = useState(inicial?.texto ?? '');
  const [activo, setActivo] = useState(inicial?.activo ?? true);
  const [validacionServidor, setValidacionServidor] = useState<{ mensaje?: string; rechazados: CaracterRechazado[] } | null>(null);

  const sospechosos = useMemo(() => caracteresSospechosos(texto), [texto]);
  const faltantes   = useMemo(() => ['{CELULAR_ASESOR}', '{LINK_ASESOR}'].filter(p => !texto.includes(p)), [texto]);
  const longitudBase = useMemo(
    () => PLACEHOLDERS.reduce((t, p) => t.split(p).join(''), texto).length,
    [texto],
  );
  const usaNombre  = texto.includes('{NOMBRE}');
  const usaCartera = texto.includes('{CARTERA}');
  const usaEmpresa = texto.includes('{EMPRESA}');
  const estimado   = longitudBase + LONGITUD_CELULAR + LONGITUD_LINK_WAME
    + (usaNombre ? NOMBRE_REFERENCIA : 0) + (usaCartera ? CARTERA_REFERENCIA : 0) + (usaEmpresa ? EMPRESA_REFERENCIA : 0);

  const validarRemoto = useMutation({
    mutationFn: () => validarMensajeAutorizado({ titulo, texto, activo }),
    onSuccess: v => setValidacionServidor(v.valido ? null : { mensaje: v.mensaje, rechazados: v.caracteresRechazados ?? [] }),
  });

  const bloqueado = titulo.trim().length === 0 || texto.trim().length === 0 || faltantes.length > 0 || sospechosos.length > 0 || guardando;

  const insertar = (ph: string) => setTexto(t => t + ph);

  // Texto con los caracteres rechazados resaltados (posiciones base 1).
  const marcados = new Set([...sospechosos, ...(validacionServidor?.rechazados ?? [])].map(r => r.posicion));

  return (
    <div style={{ display: 'flex', flexDirection: 'column', gap: 14 }}>
      <div style={{ display: 'flex', flexDirection: 'column', gap: 4 }}>
        <label style={{ fontSize: 11, fontWeight: 600, color: 'var(--z-text-muted)', textTransform: 'uppercase', letterSpacing: '0.04em' }}>Título</label>
        <input value={titulo} onChange={e => setTitulo(e.target.value)} maxLength={100} placeholder="Ej. Recordatorio de pago — cordial" style={inputStyle} />
      </div>

      <div style={{ display: 'flex', flexDirection: 'column', gap: 4 }}>
        <label style={{ fontSize: 11, fontWeight: 600, color: 'var(--z-text-muted)', textTransform: 'uppercase', letterSpacing: '0.04em' }}>Texto del SMS</label>
        <textarea value={texto} onChange={e => setTexto(e.target.value)} rows={4} maxLength={320}
          placeholder="Sr(a) {NOMBRE}, ... CONCILIE {CELULAR_ASESOR} o {LINK_ASESOR}"
          style={{ ...inputStyle, resize: 'vertical', fontFamily: 'ui-monospace, SFMono-Regular, Menlo, monospace', borderColor: sospechosos.length ? '#dc2626' : 'var(--z-border)' }} />
        <div style={{ display: 'flex', gap: 6, flexWrap: 'wrap', alignItems: 'center' }}>
          <span style={{ fontSize: 11, color: 'var(--z-text-muted)' }}>Insertar variable:</span>
          {PLACEHOLDERS.map(p => (
            <button key={p} onClick={() => insertar(p)} type="button"
              style={{ ...btnSecundario, padding: '3px 8px', fontSize: 11, fontFamily: 'ui-monospace, Menlo, monospace' }}>{p}</button>
          ))}
        </div>
      </div>

      {/* Contador en vivo */}
      <div style={{ display: 'flex', gap: 16, flexWrap: 'wrap', fontSize: 12, color: 'var(--z-text-muted)', background: 'var(--z-bg-page)', border: '0.5px solid var(--z-border)', borderRadius: 8, padding: '10px 14px' }}>
        <span>Texto fijo: <strong>{longitudBase}</strong></span>
        <span>+ celular: <strong>{LONGITUD_CELULAR}</strong></span>
        <span>+ link wa.me: <strong>{LONGITUD_LINK_WAME}</strong></span>
        {usaNombre  && <span>+ nombre (ref.): <strong>{NOMBRE_REFERENCIA}</strong></span>}
        {usaCartera && <span>+ cartera (ref.): <strong>{CARTERA_REFERENCIA}</strong></span>}
        {usaEmpresa && <span>+ empresa (ref.): <strong>{EMPRESA_REFERENCIA}</strong></span>}
        <span>≈ <strong style={{ color: estimado > LIMITE_SMS ? '#dc2626' : '#16a34a' }}>{estimado}</strong> / {LIMITE_SMS}</span>
        <span style={{ fontSize: 11 }}>(el cálculo real se hace con los nombres, carteras, empresas y enlaces de cada estrategia; {'{CARTERA}'} y {'{EMPRESA}'} requieren esas columnas en el archivo)</span>
      </div>

      {faltantes.length > 0 && (
        <div style={cajaError}>Falta {faltantes.join(' y ')}: sin ellos el titular no tiene a quién responder.</div>
      )}
      {sospechosos.length > 0 && (
        <div style={cajaError}>
          Caracteres que no caben en un SMS de 160 (tildes, ñ o símbolos): <strong>{[...new Set(sospechosos.map(s => s.caracter))].join(' ')}</strong>.
          Reemplázalos: "Señor" → "Senor", "gestión" → "gestion".
          <div style={{ marginTop: 6, fontFamily: 'ui-monospace, Menlo, monospace', fontSize: 12, whiteSpace: 'pre-wrap', color: 'var(--z-text-primary)' }}>
            {[...texto].map((c, i) => marcados.has(i + 1)
              ? <mark key={i} style={{ background: '#fecaca', color: '#dc2626', borderRadius: 2 }}>{c}</mark>
              : <span key={i}>{c}</span>)}
          </div>
        </div>
      )}
      {validacionServidor?.mensaje && sospechosos.length === 0 && <div style={cajaError}>{validacionServidor.mensaje}</div>}
      {error && <div style={cajaError}>{error}</div>}

      <label style={{ display: 'flex', alignItems: 'center', gap: 8, fontSize: 13 }}>
        <input type="checkbox" checked={activo} onChange={e => setActivo(e.target.checked)} /> Activo (visible para los líderes)
      </label>

      <div style={{ display: 'flex', gap: 10, justifyContent: 'flex-end' }}>
        <button onClick={onCancelar} style={btnSecundario}>Cancelar</button>
        <button onClick={() => validarRemoto.mutate()} disabled={texto.trim().length === 0} style={btnSecundario}>Validar</button>
        <button onClick={() => onGuardar({ titulo: titulo.trim(), texto: texto.trim(), activo })} disabled={bloqueado} style={btnPrimario(!bloqueado)}>
          {guardando ? 'Guardando…' : inicial ? 'Guardar cambios' : 'Crear mensaje'}
        </button>
      </div>
    </div>
  );
}
