// Panel del cargue que se está siguiendo. Según el estado del job muestra el progreso
// (Validando / Cargando), la vista previa (PreviewListo), el resultado (Completado) o el
// error (Error / Cancelado). El job vive en el servidor: este panel solo lo consulta.
import type { CargueJob, FaseCargueJob, ResultadoCargue } from '../../../types/cargueFuentes';
import { Aviso } from '../../../components/Aviso';
import { BarraProgreso } from '../../../components/BarraProgreso';
import { btnSecundario, cajaError } from '../historico-virtualidad/compartidos';
import { PreviewCargue } from './PreviewCargue';
import { DesgloseChips, type ChipDef } from './DesgloseChips';
import { PALETAS, duracionLegible, fechaLegible, tamanoLegible } from './formato';

// Nombre legible de cada fase del motor (el servidor manda el nombre técnico).
const FASES: Record<FaseCargueJob, string> = {
  Leyendo:     'Leyendo el archivo',
  Columnas:    'Reconociendo las columnas',
  Apoyo:       'Cargando tablas de apoyo',
  Limpieza:    'Limpiando filas',
  Dedup:       'Quitando filas repetidas',
  Cruces:      'Cruzando con la base de datos',
  Preview:     'Armando la vista previa',
  Escribiendo: 'Escribiendo en la base de datos',
  Auditoria:   'Registrando la auditoría',
  Terminado:   'Terminado',
};

export type AccionPendiente = 'confirmar' | 'cancelar' | 'descartar' | null;

export function PanelJob({ job, pendiente, onConfirmar, onCancelar, onDescartar, onNuevo }: {
  job: CargueJob;
  pendiente: AccionPendiente;
  onConfirmar: () => void;
  onCancelar: () => void;
  onDescartar: () => void;
  onNuevo: () => void;
}) {
  const enProceso = job.estado === 'Validando' || job.estado === 'Cargando';
  return (
    <div style={{
      display: 'flex', flexDirection: 'column', gap: 16, background: 'var(--z-bg-card)',
      border: '1px solid var(--z-border)', borderRadius: 8, padding: 20,
    }}>
      {/* Qué cargue es */}
      <div>
        <p style={{ fontSize: 13, fontWeight: 600, color: 'var(--z-text-primary)', margin: 0 }}>
          {job.etiqueta} — {job.nombreArchivo}
        </p>
        <p style={{ fontSize: 12, color: 'var(--z-text-muted)', margin: '2px 0 0' }}>
          {tamanoLegible(job.tamanoKB)} · subido por {job.usuario} · {fechaLegible(job.creadoEn)}
        </p>
      </div>

      {enProceso && (
        <>
          <BarraProgreso
            porcentaje={job.porcentaje}
            etiqueta={`${job.estado === 'Validando' ? 'Validando' : 'Cargando'} — ${FASES[job.fase] ?? job.fase}`}
          />
          {job.mensaje && <p style={{ fontSize: 12, color: 'var(--z-text-muted)', margin: 0 }}>{job.mensaje}</p>}
          <Aviso tipo="info">
            Puedes salir de esta página: el proceso sigue en el servidor y lo retomas desde
            'Cargues en curso'.
          </Aviso>
          {job.puedeCancelar && (
            <div>
              <button onClick={onCancelar} disabled={pendiente === 'cancelar'} style={btnSecundario}>
                {pendiente === 'cancelar' ? 'Cancelando…' : 'Cancelar'}
              </button>
            </div>
          )}
        </>
      )}

      {job.estado === 'PreviewListo' && (
        <PreviewCargue
          job={job} onConfirmar={onConfirmar} onDescartar={onDescartar}
          confirmando={pendiente === 'confirmar'} descartando={pendiente === 'descartar'}
        />
      )}

      {job.estado === 'Completado' && (
        job.resultado
          ? <TarjetaResultado resultado={job.resultado} onNuevo={onNuevo} />
          : <FinConError texto={job.mensaje ?? 'El cargue terminó, pero el servidor no devolvió el resultado.'} onNuevo={onNuevo} />
      )}

      {(job.estado === 'Error' || job.estado === 'Cancelado') && (
        <FinConError
          texto={job.error ?? job.mensaje ?? (job.estado === 'Cancelado' ? 'El cargue fue cancelado. No se escribió nada.' : 'El cargue falló.')}
          onNuevo={onNuevo}
        />
      )}
    </div>
  );
}

function FinConError({ texto, onNuevo }: { texto: string; onNuevo: () => void }) {
  return (
    <>
      <div style={cajaError}>{texto}</div>
      <div><button onClick={onNuevo} style={btnSecundario}>Nuevo cargue</button></div>
    </>
  );
}

// Métricas del resultado: mismos chips desplegables del preview, con la paleta verde de la
// tarjeta (mismo diseño que el resultado de CargueReportesPage). Los números son los conteos
// FINALES de la escritura; los grupos y ejemplos, los de la preparación que corrió al confirmar.
function chipsDelResultado(r: ResultadoCargue): ChipDef[] {
  const verde = PALETAS.resultado;
  const irrecuperables = r.nombresIrrecuperables ?? 0;
  return [
    { clave: 'Insertadas',        numero: r.insertadas,        label: 'insertadas',         paleta: verde },
    { clave: 'Refrescadas',       numero: r.refrescadas,       label: 'refrescadas',        paleta: verde },
    { clave: 'VaciasDescartadas', numero: r.descartadasVacias, label: 'vacías descartadas', paleta: verde },
    { clave: 'VaciasPurgadas',    numero: r.vaciasPurgadas,    label: 'vacías purgadas',    paleta: verde },
    { clave: 'TitularesNuevos',   numero: r.titularesNuevos,   label: 'titulares nuevos',   paleta: verde, unidad: ['titular', 'titulares'] },
    { clave: 'NombresReparados',  numero: r.nombresReparados + irrecuperables,
      label: irrecuperables > 0 ? 'nombres reparados / irrecuperables' : 'nombres reparados', paleta: verde, unidad: null },
    { clave: 'RelacionesNuevas',  numero: r.relacionesNuevas,  label: 'relaciones nuevas',  paleta: verde, unidad: ['relación', 'relaciones'] },
    { clave: 'Advertencias',      numero: r.advertencias,      label: 'advertencias',       paleta: verde, unidad: ['aviso', 'avisos'] },
  ];
}

function TarjetaResultado({ resultado: r, onNuevo }: { resultado: ResultadoCargue; onNuevo: () => void }) {
  return (
    <div style={{
      background: '#f0fdf4', border: '0.5px solid #86efac', borderRadius: 8, padding: 24,
      display: 'flex', flexDirection: 'column', gap: 16,
    }}>
      <p style={{ fontSize: 15, fontWeight: 600, color: '#15803d', margin: 0 }}>Cargue completado</p>
      <DesgloseChips key={r.ejecucionID} chips={chipsDelResultado(r)} desglose={r.desglose} />
      {r.mensaje && <p style={{ fontSize: 13, color: '#166534', margin: 0 }}>{r.mensaje}</p>}
      <p style={{ fontSize: 12, color: '#166534', margin: 0, lineHeight: 1.6 }}>
        Ejecución <strong>#{r.ejecucionID}</strong> · duración {duracionLegible(r.duracionSegundos)} · fecha de
        consulta <strong>{fechaLegible(r.selloFechaConsulta)}</strong>. El número de ejecución identifica este
        cargue por si hay que revertirlo.
      </p>
      <button
        onClick={onNuevo}
        style={{
          padding: '8px 16px', borderRadius: 6, border: '0.5px solid #86efac', background: '#fff',
          color: '#15803d', fontSize: 13, fontWeight: 600, cursor: 'pointer', width: 'fit-content',
        }}
      >
        Nuevo cargue
      </button>
    </div>
  );
}
