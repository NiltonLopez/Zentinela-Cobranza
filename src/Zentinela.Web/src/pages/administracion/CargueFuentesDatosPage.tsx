import { useEffect, useRef, useState } from 'react';
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query';
import {
  obtenerFuentesCargue, iniciarValidacion, obtenerJob,
  confirmarJob, cancelarJob, descartarJob,
} from '../../api/cargueFuentesApi';
import { CLAVE_JOB_CARGUE, type CargueJob, type FuenteCargue } from '../../types/cargueFuentes';
import { CargandoSpiral } from '../../components/CargandoSpiral';
import { ZDropdown } from '../../components/ZDropdown';
import { Aviso } from '../../components/Aviso';
import { BarraProgreso } from '../../components/BarraProgreso';
import { DropZone, btnSecundario, cajaError } from './historico-virtualidad/compartidos';
import { ListaCargues } from './cargue-fuentes/ListaCargues';
import { ColumnasEsperadas } from './cargue-fuentes/ColumnasEsperadas';
import { PanelJob } from './cargue-fuentes/PanelJob';
import { es404, mensajeDeError, num } from './cargue-fuentes/formato';

// ── Persistencia del job que se está siguiendo ──────────────────────────────
// El job vive en el servidor (en memoria, hasta 48 h). En sessionStorage solo se guarda su
// id para que al salir de la página —o con F5— se retome donde iba. A diferencia de
// Auditoría de Gestiones NO se limpia en 'beforeunload': retomarlo es gratis.

function leerJobGuardado(): string | null {
  try { return sessionStorage.getItem(CLAVE_JOB_CARGUE); } catch { return null; }
}

function guardarJob(id: string | null) {
  try {
    if (id) sessionStorage.setItem(CLAVE_JOB_CARGUE, id);
    else sessionStorage.removeItem(CLAVE_JOB_CARGUE);
  } catch { /* sessionStorage no disponible: solo se pierde el "retomar al volver" */ }
}

const etiquetaPaso: React.CSSProperties = {
  fontSize: 11, fontWeight: 600, color: 'var(--z-text-muted)', textTransform: 'uppercase',
  letterSpacing: '0.04em', margin: 0,
};

const extension = (nombre: string) => {
  const i = nombre.lastIndexOf('.');
  return i >= 0 ? nombre.slice(i).toLowerCase() : '';
};

type Accion = 'confirmar' | 'cancelar' | 'descartar';

// ── Página ──────────────────────────────────────────────────────────────────

export function CargueFuentesDatosPage() {
  const queryClient = useQueryClient();
  const inputRef = useRef<HTMLInputElement>(null);
  const [jobId, setJobId]         = useState<string | null>(() => leerJobGuardado());
  const [fuenteSel, setFuenteSel] = useState('');
  const [archivo, setArchivo]     = useState<File | null>(null);
  const [subida, setSubida]       = useState(0);       // % de SUBIDA del archivo (no del proceso)
  const [error, setError]         = useState('');
  const [aviso, setAviso]         = useState('');

  const fijarJob = (id: string | null) => { guardarJob(id); setJobId(id); };

  const fuentesQ = useQuery({
    queryKey: ['cargue-fuentes-fuentes'],
    queryFn:  obtenerFuentesCargue,
    staleTime: 10 * 60 * 1000,
  });
  const fuentes = fuentesQ.data ?? [];
  // Sin selección explícita se propone la primera fuente habilitada (derivado, sin efecto).
  const fuente: FuenteCargue | undefined =
    fuentes.find(f => f.fuente === fuenteSel && f.habilitada) ?? fuentes.find(f => f.habilitada);

  // El job se consulta cada 1,5 s solo mientras el servidor trabaja; en PreviewListo y en
  // los estados finales el polling se detiene. Un 404 = el job ya no existe (purga de 48 h o
  // reinicio del servidor): se olvida y se avisa, sin reintentos.
  const jobQ = useQuery({
    queryKey: ['cargue-fuentes-job', jobId],
    queryFn:  async (): Promise<CargueJob | null> => {
      try {
        return await obtenerJob(jobId!);
      } catch (err) {
        if (!es404(err)) throw err;
        fijarJob(null);
        setAviso('El cargue ya no está disponible (se purgó o el servidor se reinició). Si no se había confirmado, vuelve a subir el archivo.');
        return null;
      }
    },
    enabled:  !!jobId,
    refetchInterval: q => {
      const e = q.state.data?.estado;
      return e === 'Validando' || e === 'Cargando' ? 1500 : false;
    },
    gcTime: 48 * 60 * 60 * 1000,
    staleTime: 0,
    retry: 1,
    refetchOnWindowFocus: true,
  });
  const job = jobId ? jobQ.data ?? null : null;

  // Cada cambio de estado del job (sobre todo al llegar a Completado) refresca la lista.
  const estadoJob = job?.estado;
  useEffect(() => {
    if (estadoJob) queryClient.invalidateQueries({ queryKey: ['cargue-fuentes-jobs'] });
  }, [estadoJob, queryClient]);

  // Deja el job recibido en la caché de su query, sin esperar al siguiente polling.
  const publicarJob = (j: CargueJob) => queryClient.setQueryData(['cargue-fuentes-job', j.jobId], j);

  function limpiarArchivo() {
    setArchivo(null);
    if (inputRef.current) inputRef.current.value = '';
  }

  const mutValidar = useMutation({
    mutationFn: ({ f, a }: { f: string; a: File }) => iniciarValidacion(f, a, setSubida),
    onSuccess: (j) => {
      publicarJob(j);
      fijarJob(j.jobId);
      limpiarArchivo();
      queryClient.invalidateQueries({ queryKey: ['cargue-fuentes-jobs'] });
    },
    onError: (err) => {
      setError(mensajeDeError(err,
        'No se pudo subir el archivo. Intenta de nuevo.',
        'Ya hay un cargue de esta fuente en curso. Retómalo desde la lista de cargues.'));
      limpiarArchivo();
    },
  });

  // Confirmar / cancelar devuelven el job actualizado; descartar (DELETE → 204) no devuelve nada.
  const mutAccion = useMutation({
    mutationFn: async (accion: Accion): Promise<CargueJob | null> => {
      if (accion === 'confirmar') return confirmarJob(jobId!);
      if (accion === 'cancelar')  return cancelarJob(jobId!);
      await descartarJob(jobId!);
      return null;
    },
    onSuccess: (j) => {
      setError('');
      if (j) publicarJob(j);
      else fijarJob(null);   // descartado: se vuelve al inicio
      queryClient.invalidateQueries({ queryKey: ['cargue-fuentes-jobs'] });
    },
    onError: (err) => {
      setError(mensajeDeError(err, 'No se pudo completar la acción sobre el cargue.',
        'El cargue cambió de estado mientras tanto. Revisa su estado actual.'));
      // Un 409 suele significar que el estado cambió: se refresca para mostrar el real.
      queryClient.invalidateQueries({ queryKey: ['cargue-fuentes-job', jobId] });
    },
  });
  const pendiente = mutAccion.isPending ? mutAccion.variables ?? null : null;

  const seleccionar = (e: React.ChangeEvent<HTMLInputElement>) => {
    const a = e.target.files?.[0];
    if (!a || !fuente) return;
    setError(''); setAviso('');
    if (!fuente.extensionesAceptadas.map(x => x.toLowerCase()).includes(extension(a.name))) {
      setError(`Formato no admitido para ${fuente.etiqueta}. Usa ${fuente.extensionesAceptadas.join(' o ')}.`);
      limpiarArchivo();
      return;
    }
    setArchivo(a);
    setSubida(0);
    // Se valida de inmediato: el servidor lo procesa en segundo plano y devuelve el job.
    mutValidar.mutate({ f: fuente.fuente, a });
  };

  const retomar     = (id: string) => { setError(''); setAviso(''); fijarJob(id); };
  const nuevoCargue = () => { setError(''); fijarJob(null); };

  const opciones = fuentes.map(f => ({
    value: f.fuente,
    label: f.habilitada ? f.etiqueta : `${f.etiqueta} (próximamente)`,
    sublabel: 'Columnas: ' + f.columnasEsperadas.map(c => c.nombre).join(', '),
  }));

  const btnChico: React.CSSProperties = { ...btnSecundario, padding: '4px 10px', marginLeft: 8 };

  return (
    <div style={{ display: 'flex', flexDirection: 'column', gap: 24, maxWidth: 1000 }}>
      {/* Encabezado */}
      <div style={{ display: 'flex', justifyContent: 'space-between', alignItems: 'flex-start', flexWrap: 'wrap', gap: 12 }}>
        <div>
          <h1 style={{ fontSize: 20, fontWeight: 600, color: 'var(--z-text-primary)', margin: 0 }}>Cargue de Fuentes de Datos</h1>
          <p style={{ fontSize: 13, color: 'var(--z-text-muted)', margin: '4px 0 0' }}>
            Pre-carga, validación e ingreso de los archivos de contactabilidad que entrega el equipo de
            localización. El archivo se procesa en el servidor: revisa la vista previa y confirma.
          </p>
        </div>
      </div>

      <ListaCargues jobActual={jobId} onRetomar={retomar} />

      {aviso && <Aviso tipo="warning">{aviso}</Aviso>}
      {error && <div style={cajaError}>{error}</div>}

      {/* ── Job activo ── */}
      {jobId && !job && (jobQ.isPending || jobQ.isFetching) && (
        <CargandoSpiral mensaje="Consultando el estado del cargue…" />
      )}
      {jobId && !job && jobQ.isError && !jobQ.isFetching && (
        <div style={cajaError}>
          No se pudo consultar el estado del cargue.
          <button onClick={() => jobQ.refetch()} style={btnChico}>Reintentar</button>
          <button onClick={nuevoCargue} style={btnChico}>Olvidar este cargue</button>
        </div>
      )}
      {job && (
        <PanelJob
          job={job}
          pendiente={pendiente}
          onConfirmar={() => mutAccion.mutate('confirmar')}
          onCancelar={() => mutAccion.mutate('cancelar')}
          onDescartar={() => mutAccion.mutate('descartar')}
          onNuevo={nuevoCargue}
        />
      )}

      {/* ── Sin job: elegir la fuente y subir el archivo ── */}
      {!jobId && (
        <>
          {fuentesQ.isLoading && <CargandoSpiral mensaje="Cargando fuentes…" />}
          {fuentesQ.isError && <div style={cajaError}>No se pudo cargar el catálogo de fuentes.</div>}

          {fuentes.length > 0 && (
            <div style={{ display: 'flex', flexDirection: 'column', gap: 6 }}>
              <p style={etiquetaPaso}>Paso 1 — Elige la fuente</p>
              <ZDropdown<string>
                options={opciones}
                value={fuente?.fuente ?? ''}
                // Las fuentes aún no integradas se ven, pero no se pueden elegir.
                onChange={v => { if (fuentes.find(f => f.fuente === v)?.habilitada) { setFuenteSel(v); setError(''); } }}
                disabled={mutValidar.isPending}
                style={{ minWidth: 280 }}
              />
              {!fuente && (
                <p style={{ fontSize: 12, color: 'var(--z-text-muted)', margin: 0 }}>Todavía no hay fuentes habilitadas para cargar.</p>
              )}
            </div>
          )}

          {fuente && (
            <>
              <div style={{ display: 'flex', flexDirection: 'column', gap: 6 }}>
                <p style={etiquetaPaso}>Paso 2 — Sube el archivo</p>
                <DropZone
                  archivoNombre={archivo?.name ?? ''}
                  archivoKB={(archivo?.size ?? 0) / 1024}
                  inputRef={inputRef}
                  onClick={() => inputRef.current?.click()}
                  onChange={seleccionar}
                  onQuitar={limpiarArchivo}
                  disabled={mutValidar.isPending}
                  accept={fuente.extensionesAceptadas.join(',') || '.csv,.xlsx'}
                  textoVacio={
                    `Haz clic para seleccionar un archivo ${fuente.extensionesAceptadas.join(' o ')}. ` +
                    `Un XLSX admite hasta ${num(fuente.limiteFilasXlsx)} filas; para archivos grandes usa CSV (más rápido y sin ese límite).`
                  }
                />
                {mutValidar.isPending && (
                  <BarraProgreso
                    porcentaje={subida}
                    etiqueta={subida < 100 ? 'Subiendo el archivo' : 'Archivo subido — iniciando la validación'}
                  />
                )}
              </div>

              <ColumnasEsperadas fuente={fuente} />
            </>
          )}
        </>
      )}
    </div>
  );
}
