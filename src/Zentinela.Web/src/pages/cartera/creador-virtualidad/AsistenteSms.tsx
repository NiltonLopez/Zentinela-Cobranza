import { useReducer } from 'react';
import { useQuery } from '@tanstack/react-query';
import { useAuth } from '../../../context/AuthContext';
import { ZDropdown } from '../../../components/ZDropdown';
import { obtenerLideresDropdown } from '../../../api/dashboardApi';
import { reducer, estadoInicial, type Paso } from './estadoAsistente';
import { Stepper, Aviso } from './compartidosSms';
import { PasoCargarArchivo } from './PasoCargarArchivo';
import { PasoEnlaces } from './PasoEnlaces';
import { PasoMensaje } from './PasoMensaje';
import { PasoGenerar } from './PasoGenerar';

/**
 * Asistente de 4 pasos del canal SMS (ADR-037). El estado vive en un reducer con
 * invalidación en cascada; el archivo se conserva en memoria y se re-sube en cada paso.
 *
 * Alcance: el Líder no ve selector (es su estrategia; el servidor usa el liderID del
 * token). Director y Admin eligen para qué líder generan.
 */
export function AsistenteSms() {
  const { usuario } = useAuth();
  const esLider = usuario?.rol === 'Líder';
  const [estado, dispatch] = useReducer(reducer, esLider ? (usuario?.liderID ?? null) : null, estadoInicial);

  const { data: lideres = [], isLoading: cargandoLideres } = useQuery({
    queryKey: ['sms-lideres-dropdown'],
    queryFn:  obtenerLideresDropdown,
    enabled:  !esLider,
    staleTime: 10 * 60 * 1000,
  });

  const liderListo = estado.liderID != null;

  // Hasta dónde puede volver: el paso más avanzado cuyos requisitos siguen cumplidos.
  const maxAlcanzado: Paso =
    estado.mensajeID != null ? 4 :
    estado.catalogo          ? 3 :
    estado.validacion        ? 2 : 1;

  const irA = (p: Paso) => dispatch({ tipo: 'IR_A', paso: p });

  return (
    <div style={{ display: 'flex', flexDirection: 'column', gap: 22 }}>

      {!esLider && (
        <div style={{ display: 'flex', flexDirection: 'column', gap: 6 }}>
          <label style={{ fontSize: 11, fontWeight: 600, color: 'var(--z-text-muted)', textTransform: 'uppercase', letterSpacing: '0.04em' }}>
            Estrategia para el líder
          </label>
          <ZDropdown
            options={[{ value: 0, label: cargandoLideres ? 'Cargando líderes…' : 'Selecciona un líder…' }, ...lideres.map(l => ({ value: l.id, label: l.nombre }))]}
            value={estado.liderID ?? 0}
            onChange={v => dispatch({ tipo: 'SET_LIDER', liderID: v === 0 ? null : v })}
            background="var(--z-bg-page)"
            style={{ minWidth: 300 }}
            disabled={cargandoLideres}
          />
          {estado.paso > 1 && (
            <span style={{ fontSize: 11, color: 'var(--z-text-muted)' }}>Cambiar el líder reinicia el asistente.</span>
          )}
        </div>
      )}

      {esLider && !liderListo && (
        <Aviso tipo="danger">Tu usuario no tiene un líder asociado; no es posible generar estrategias. Contacta al administrador.</Aviso>
      )}

      <Stepper actual={estado.paso} maxAlcanzado={maxAlcanzado} onIr={irA} />

      <div style={{ background: 'var(--z-bg-card)', border: '1px solid var(--z-border)', borderRadius: 10, padding: 24 }}>
        {estado.paso === 1 && <PasoCargarArchivo estado={estado} dispatch={dispatch} liderListo={liderListo} />}
        {estado.paso === 2 && <PasoEnlaces estado={estado} dispatch={dispatch} />}
        {estado.paso === 3 && <PasoMensaje estado={estado} dispatch={dispatch} />}
        {estado.paso === 4 && <PasoGenerar estado={estado} dispatch={dispatch} />}
      </div>
    </div>
  );
}
