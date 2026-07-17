import { useState, useEffect, useRef } from 'react';
import { useQuery, useMutation } from '@tanstack/react-query';
import {
  listarCarterasPagos,
  registrarPagoIndividual,
  buscarAsesoresCartera,
  buscarTitularEnCartera,
} from '../../../api/pagosApi';
import type { AsesorCartera, RegistrarPagoIndividualResponse } from '../../../types/pagos';
import { CargandoSpiral } from '../../../components/CargandoSpiral';
import { ZDropdown } from '../../../components/ZDropdown';

// ── Estilos compartidos ─────────────────────────────────────────────────────────

const LABEL: React.CSSProperties = {
  fontSize: '11px', fontWeight: 600, color: 'var(--z-text-muted)',
  textTransform: 'uppercase', letterSpacing: '0.04em', marginBottom: 6, display: 'block',
};
const INPUT: React.CSSProperties = {
  width: '100%', padding: '8px 11px', fontSize: '13px',
  border: '1px solid var(--z-border)', borderRadius: '6px',
  background: 'var(--z-bg-page)', color: 'var(--z-text-primary)',
  outline: 'none', boxSizing: 'border-box',
};

const cop = (v: number) =>
  new Intl.NumberFormat('es-CO', { style: 'currency', currency: 'COP', maximumFractionDigits: 0 }).format(v);

const extraerMensaje = (err: unknown) =>
  (err as { response?: { data?: { mensaje?: string } } })?.response?.data?.mensaje
  ?? 'No se pudo registrar el pago. Intenta de nuevo.';

// ── Input de moneda (formato COP, solo dígitos) ─────────────────────────────────

function InputMoneda({ value, onChange, placeholder }: {
  value: string; onChange: (v: string) => void; placeholder?: string;
}) {
  const display = value === '' ? '' : Number(value).toLocaleString('es-CO');
  return (
    <input
      type="text"
      inputMode="numeric"
      value={display}
      placeholder={placeholder ?? '0'}
      onChange={(e) => onChange(e.target.value.replace(/\D/g, ''))}
      style={INPUT}
      onFocus={(e) => { e.currentTarget.style.borderColor = 'var(--z-brand)'; }}
      onBlur={(e) => { e.currentTarget.style.borderColor = 'var(--z-border)'; }}
    />
  );
}

// ── Formulario de pago individual ────────────────────────────────────────────────

export function PagoIndividualForm() {
  const [carteraID, setCarteraID] = useState<number | ''>('');
  const [cedula,    setCedula]    = useState('');
  const [nombre,    setNombre]    = useState('');
  const [valor,     setValor]     = useState('');
  const [honorarios, setHonorarios] = useState('');
  const [fecha,     setFecha]     = useState('');
  const [esBaseGeneral, setEsBaseGeneral] = useState(false);

  // Estado del lookup de titular
  const [titularNoExiste, setTitularNoExiste] = useState(false);
  const [buscandoTitular, setBuscandoTitular] = useState(false);

  // Autocomplete de asesor
  const [asesorTexto, setAsesorTexto] = useState('');
  const [asesorSel,   setAsesorSel]   = useState<AsesorCartera | null>(null);
  const [sugerencias, setSugerencias] = useState<AsesorCartera[]>([]);
  const [mostrarSug,  setMostrarSug]  = useState(false);

  const [resultado, setResultado] = useState<RegistrarPagoIndividualResponse | null>(null);
  const [errorMensaje, setErrorMensaje] = useState('');

  const debounceTitular = useRef<ReturnType<typeof setTimeout> | null>(null);
  const debounceAsesor  = useRef<ReturnType<typeof setTimeout> | null>(null);

  const { data: carteras = [] } = useQuery({
    queryKey: ['pagos-carteras'],
    queryFn:  listarCarterasPagos,
  });

  // Lookup del titular al cambiar cédula o cartera: autocompleta el nombre o avisa que no existe.
  useEffect(() => {
    setTitularNoExiste(false);
    if (debounceTitular.current) clearTimeout(debounceTitular.current);

    const ced = Number(cedula);
    if (!carteraID || !ced || cedula.length < 6) { return; }

    setBuscandoTitular(true);
    debounceTitular.current = setTimeout(async () => {
      try {
        const r = await buscarTitularEnCartera(Number(carteraID), ced);
        if (r.existe) { setNombre(r.nombre ?? ''); setTitularNoExiste(false); }
        else          { setNombre(''); setTitularNoExiste(true); }
      } catch {
        setTitularNoExiste(false);
      } finally {
        setBuscandoTitular(false);
      }
    }, 400);
  }, [carteraID, cedula]);

  // Autocomplete de asesor (debounce). No corre si es BASE GENERAL o ya hay uno seleccionado.
  useEffect(() => {
    if (asesorSel) return;
    if (debounceAsesor.current) clearTimeout(debounceAsesor.current);
    if (esBaseGeneral || !carteraID || !asesorTexto.trim()) { setSugerencias([]); return; }

    debounceAsesor.current = setTimeout(async () => {
      const r = await buscarAsesoresCartera(Number(carteraID), asesorTexto.trim());
      setSugerencias(r);
      setMostrarSug(true);
    }, 250);
  }, [asesorTexto, asesorSel, carteraID, esBaseGeneral]);

  const mut = useMutation({
    mutationFn: () => registrarPagoIndividual({
      carteraID:     Number(carteraID),
      cedulaTitular: Number(cedula),
      nombreCliente: nombre.trim(),
      valorPagado:   Number(valor),
      fechaPago:     fecha,
      honorarios:    Number(honorarios || '0'),
      usuarioControl: esBaseGeneral ? null : (asesorSel?.usuarioControl ?? null),
      esBaseGeneral,
    }),
    onSuccess: (data) => { setResultado(data); setErrorMensaje(''); },
    onError:   (err)  => { setErrorMensaje(extraerMensaje(err)); },
  });

  function cambiarCartera(v: number | '') {
    setCarteraID(v);
    // Cambiar de cartera invalida titular y asesor (dependen de la cartera).
    setNombre(''); setTitularNoExiste(false);
    limpiarAsesor();
  }

  function limpiarAsesor() {
    setAsesorSel(null); setAsesorTexto(''); setSugerencias([]); setMostrarSug(false);
  }

  function seleccionarAsesor(a: AsesorCartera) {
    setAsesorSel(a); setAsesorTexto(a.nombre); setSugerencias([]); setMostrarSug(false);
  }

  function reiniciar() {
    setCarteraID(''); setCedula(''); setNombre(''); setValor(''); setHonorarios('');
    setFecha(''); setEsBaseGeneral(false); setTitularNoExiste(false);
    limpiarAsesor(); setResultado(null); setErrorMensaje('');
  }

  const puedeRegistrar =
    !!carteraID && cedula.length >= 6 && !titularNoExiste && !buscandoTitular &&
    !!nombre.trim() && Number(valor) > 0 && !!fecha &&
    (esBaseGeneral || !!asesorSel) && !mut.isPending;

  if (mut.isPending) {
    return <CargandoSpiral mensaje="Registrando pago..." />;
  }

  // ── Resultado ──────────────────────────────────────────────────────────────
  if (resultado) {
    return (
      <div style={{
        background: '#f0fdf4', border: '0.5px solid #86efac',
        borderRadius: '8px', padding: '24px',
        display: 'flex', flexDirection: 'column', gap: '14px', maxWidth: 520,
      }}>
        <p style={{ fontSize: '15px', fontWeight: 600, color: '#15803d', margin: 0 }}>
          Pago registrado
        </p>
        <div style={{ display: 'flex', flexDirection: 'column', gap: 6, fontSize: 13, color: '#166534' }}>
          <span>Líder asignado: <strong>{resultado.liderNombre || `#${resultado.liderID}`}</strong></span>
          <span>
            Asesor: <strong>{resultado.esBaseGeneral ? 'BASE GENERAL (sin asesor)' : (asesorSel?.nombre ?? '—')}</strong>
          </span>
        </div>
        <button
          onClick={reiniciar}
          style={{
            padding: '8px 16px', borderRadius: '6px',
            border: '0.5px solid #86efac', background: '#fff',
            color: '#15803d', fontSize: '13px', fontWeight: 600,
            cursor: 'pointer', width: 'fit-content',
          }}
        >
          Registrar otro pago
        </button>
      </div>
    );
  }

  // ── Formulario ─────────────────────────────────────────────────────────────
  return (
    <div style={{
      background: 'var(--z-bg-card)', border: '0.5px solid var(--z-border)',
      borderRadius: '8px', padding: '24px', maxWidth: 560,
      display: 'flex', flexDirection: 'column', gap: '16px',
    }}>
      {/* Cartera */}
      <div>
        <label style={LABEL}>Cartera</label>
        <ZDropdown
          options={[{ value: '', label: '— Seleccionar cartera —' }, ...carteras.map(c => ({ value: c.id, label: c.nombre }))]}
          value={carteraID}
          onChange={cambiarCartera}
          background="var(--z-bg-page)"
        />
      </div>

      {/* Cédula */}
      <div>
        <label style={LABEL}>Cédula del titular</label>
        <input
          type="text"
          inputMode="numeric"
          value={cedula}
          placeholder="Número de documento"
          disabled={!carteraID}
          onChange={(e) => setCedula(e.target.value.replace(/\D/g, ''))}
          style={{ ...INPUT, opacity: carteraID ? 1 : 0.5 }}
          onFocus={(e) => { e.currentTarget.style.borderColor = 'var(--z-brand)'; }}
          onBlur={(e) => { e.currentTarget.style.borderColor = 'var(--z-border)'; }}
        />
        {!carteraID && (
          <p style={{ fontSize: 11, color: 'var(--z-text-muted)', margin: '4px 0 0' }}>
            Selecciona primero la cartera.
          </p>
        )}
        {buscandoTitular && (
          <p style={{ fontSize: 11, color: 'var(--z-text-muted)', margin: '4px 0 0' }}>Buscando titular…</p>
        )}
        {titularNoExiste && (
          <p style={{ fontSize: 12, color: '#dc2626', margin: '4px 0 0' }}>
            El titular no existe en la BD en la cartera seleccionada.
          </p>
        )}
      </div>

      {/* Nombre cliente (autocompletado, editable) */}
      <div>
        <label style={LABEL}>Nombre del cliente</label>
        <input
          type="text"
          value={nombre}
          placeholder="Se completa al validar la cédula"
          disabled={!carteraID}
          onChange={(e) => setNombre(e.target.value)}
          style={{ ...INPUT, opacity: carteraID ? 1 : 0.5 }}
          onFocus={(e) => { e.currentTarget.style.borderColor = 'var(--z-brand)'; }}
          onBlur={(e) => { e.currentTarget.style.borderColor = 'var(--z-border)'; }}
        />
      </div>

      {/* Valor + Honorarios */}
      <div style={{ display: 'flex', gap: 12, flexWrap: 'wrap' }}>
        <div style={{ flex: 1, minWidth: 180 }}>
          <label style={LABEL}>Valor pagado</label>
          <InputMoneda value={valor} onChange={setValor} />
        </div>
        <div style={{ flex: 1, minWidth: 180 }}>
          <label style={LABEL}>Honorarios</label>
          <InputMoneda value={honorarios} onChange={setHonorarios} />
        </div>
      </div>

      {/* Fecha */}
      <div>
        <label style={LABEL}>Fecha de pago</label>
        <input
          type="date"
          value={fecha}
          onChange={(e) => setFecha(e.target.value)}
          style={{ ...INPUT, cursor: 'text' }}
          onFocus={(e) => { e.currentTarget.style.borderColor = 'var(--z-brand)'; }}
          onBlur={(e) => { e.currentTarget.style.borderColor = 'var(--z-border)'; }}
        />
      </div>

      {/* Asesor (autocomplete) */}
      <div>
        <label style={LABEL}>Asesor (Usuario Control)</label>
        <div style={{ position: 'relative' }}>
          <input
            type="text"
            value={asesorTexto}
            placeholder={esBaseGeneral ? 'Sin asesor (BASE GENERAL)' : 'Escribe el nombre del asesor…'}
            disabled={esBaseGeneral || !carteraID}
            onChange={(e) => { setAsesorTexto(e.target.value); setAsesorSel(null); }}
            onFocus={(e) => {
              e.currentTarget.style.borderColor = 'var(--z-brand)';
              if (sugerencias.length > 0) setMostrarSug(true);
            }}
            onBlur={(e) => {
              e.currentTarget.style.borderColor = 'var(--z-border)';
              setTimeout(() => setMostrarSug(false), 150);
            }}
            style={{
              ...INPUT,
              opacity: (esBaseGeneral || !carteraID) ? 0.5 : 1,
              borderColor: asesorSel ? 'var(--z-brand)' : 'var(--z-border)',
            }}
          />
          {asesorSel && !esBaseGeneral && (
            <button
              onClick={limpiarAsesor}
              style={{
                position: 'absolute', right: 8, top: '50%', transform: 'translateY(-50%)',
                background: 'none', border: 'none', cursor: 'pointer',
                fontSize: 14, color: 'var(--z-text-muted)', lineHeight: 1,
              }}
            >×</button>
          )}
          {mostrarSug && !esBaseGeneral && sugerencias.length > 0 && (
            <div style={{
              position: 'absolute', top: '100%', left: 0, right: 0,
              background: 'var(--z-bg-card)', border: '0.5px solid var(--z-border)',
              borderRadius: 6, boxShadow: '0 4px 16px rgba(0,0,0,0.1)',
              zIndex: 10, marginTop: 2, overflow: 'hidden',
            }}>
              {sugerencias.map((a, i) => (
                <div
                  key={a.agenteID}
                  onMouseDown={() => seleccionarAsesor(a)}
                  style={{
                    padding: '9px 12px', fontSize: 13, cursor: 'pointer',
                    color: 'var(--z-text-primary)',
                    borderBottom: i < sugerencias.length - 1 ? '0.5px solid var(--z-border)' : 'none',
                  }}
                  onMouseOver={(e) => { (e.currentTarget as HTMLElement).style.background = 'var(--z-bg-surface)'; }}
                  onMouseOut={(e) => { (e.currentTarget as HTMLElement).style.background = 'transparent'; }}
                >
                  {a.nombre}
                  <span style={{ color: 'var(--z-text-muted)', marginLeft: 8, fontSize: 11, fontFamily: 'monospace' }}>
                    {a.usuarioControl}
                  </span>
                </div>
              ))}
            </div>
          )}
        </div>
        {/* BASE GENERAL */}
        <label style={{
          display: 'flex', alignItems: 'center', gap: 8, marginTop: 10,
          cursor: 'pointer', fontSize: 12, color: 'var(--z-text-muted)',
        }}>
          <input
            type="checkbox"
            checked={esBaseGeneral}
            onChange={(e) => { setEsBaseGeneral(e.target.checked); if (e.target.checked) limpiarAsesor(); }}
            style={{ cursor: 'pointer' }}
          />
          <span>BASE GENERAL — pago sin asesor (se atribuye solo al líder)</span>
        </label>
      </div>

      {/* Resumen del valor */}
      {Number(valor) > 0 && (
        <p style={{ fontSize: 12, color: 'var(--z-text-muted)', margin: 0 }}>
          Se registrará un pago de <strong>{cop(Number(valor))}</strong>
          {Number(honorarios) > 0 && <> · honorarios <strong>{cop(Number(honorarios))}</strong></>}.
        </p>
      )}

      {errorMensaje && (
        <div style={{
          background: '#fef2f2', border: '0.5px solid #fca5a5',
          borderRadius: 6, padding: '10px 14px', fontSize: 13, color: '#dc2626',
        }}>
          {errorMensaje}
        </div>
      )}

      <div>
        <button
          onClick={() => mut.mutate()}
          disabled={!puedeRegistrar}
          style={{
            padding: '9px 20px', borderRadius: '6px', border: 'none',
            background: puedeRegistrar ? 'var(--z-brand)' : 'var(--z-border)',
            color: puedeRegistrar ? '#fff' : 'var(--z-text-muted)',
            fontSize: '13px', fontWeight: 600,
            cursor: puedeRegistrar ? 'pointer' : 'not-allowed',
          }}
        >
          Registrar pago
        </button>
      </div>
    </div>
  );
}
