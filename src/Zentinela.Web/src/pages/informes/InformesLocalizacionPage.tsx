import { useCallback, useEffect, useRef, useState } from 'react';
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query';
import { useAuth } from '../../context/AuthContext';
import { useAlcanceLider } from '../../hooks/useAlcanceLider';
import {
  obtenerEstadoLocalizacion, problemaLocalizacion, recalcularLocalizacion,
} from '../../api/informesLocalizacionApi';
import { C, Cargando, ErrorMsg, ModalColumnas } from '../../components/informes/reporteCompartido';
import type { DownloadFactory, DownloadRegistrar, PdfExportDescriptor } from '../../components/informes/reporteCompartido';
import { TabResumen } from './localizacion/TabResumen';
import { TabCobertura } from './localizacion/TabCobertura';
import { TabFrescura } from './localizacion/TabFrescura';
import { TabRankings } from './localizacion/TabRankings';
import { TabSugerencias } from './localizacion/TabSugerencias';
import { fmtFechaHora, type Alcance } from './localizacion/compartidos';

type Tab = 'resumen' | 'cobertura' | 'frescura' | 'rankings' | 'sugerencias';

const TABS: { id: Tab; label: string }[] = [
  { id: 'resumen',   label: 'Resumen' },
  { id: 'cobertura', label: 'Cobertura por fuente' },
  { id: 'frescura',  label: 'Frescura' },
  { id: 'rankings',  label: 'Rankings' },
  { id: 'sugerencias', label: 'Sugerencias' },
];

const MOTIVO: Record<string, string> = {
  ARRANQUE: 'al arrancar',
  CORTE_13H: 'corte de las 13:00',
  CARGUE_FUENTE: 'tras un cargue de fuente',
  MANUAL: 'recálculo manual',
};

// ╔══════════════════════════════════════════════════════════════════════════════╗
// ║  PÁGINA — Informes de Localización (ADR-040)                                  ║
// ║  Todo lee el último corte del precálculo. El estado del corte decide qué se   ║
// ║  muestra: sin tablas (503), calculando el primero, o las pestañas.           ║
// ╚══════════════════════════════════════════════════════════════════════════════╝
export function InformesLocalizacionPage() {
  const { usuario } = useAuth();
  const esAdmin = usuario?.rol === 'Admin';
  const { liderPropio } = useAlcanceLider();
  const queryClient = useQueryClient();

  const [tabActiva, setTabActiva] = useState<Tab>('resumen');
  // Alcance compartido por Resumen, Cobertura y Frescura (para el Líder, fijo a su liderID).
  const [alcance, setAlcance] = useState<Alcance>({ liderID: liderPropio, carteraID: 0 });
  const [descargando, setDescargando] = useState(false);
  const [modalCols, setModalCols] = useState<PdfExportDescriptor | null>(null);
  const downloadFnRef = useRef<DownloadFactory | null>(null);

  const handleRegisterDownload = useCallback<DownloadRegistrar>((fn) => {
    downloadFnRef.current = fn;
  }, []);

  const handleDescargar = () => {
    if (!downloadFnRef.current || descargando) return;
    const descriptor = downloadFnRef.current();
    if (!descriptor) return;
    setModalCols(descriptor);
  };

  // Estado del precálculo: se consulta seguido mientras haya un corte calculándose.
  const estado = useQuery({
    queryKey: ['loc-estado'],
    queryFn: obtenerEstadoLocalizacion,
    refetchInterval: q => (q.state.data?.calculando || !q.state.data?.ultimoOk ? 15_000 : 5 * 60_000),
    retry: (n, err) => problemaLocalizacion(err) == null && n < 2,
  });

  // Cuando aparece un corte nuevo, las pestañas vuelven a leer.
  const corteID = estado.data?.ultimoOk?.corteID;
  const corteVisto = useRef<number | undefined>(undefined);
  useEffect(() => {
    if (corteID === undefined) return;
    if (corteVisto.current !== undefined && corteVisto.current !== corteID) {
      void queryClient.invalidateQueries({ predicate: q => String(q.queryKey[0]).startsWith('loc-') && q.queryKey[0] !== 'loc-estado' });
    }
    corteVisto.current = corteID;
  }, [corteID, queryClient]);

  const recalcular = useMutation({
    mutationFn: recalcularLocalizacion,
    onSettled: () => { void queryClient.invalidateQueries({ queryKey: ['loc-estado'] }); },
  });

  const problema = estado.isError ? problemaLocalizacion(estado.error) : null;
  const e = estado.data;
  // Las Sugerencias no dependen del corte: su PDF se puede bajar aunque no haya precálculo.
  const sinDescarga = !e?.ultimoOk && tabActiva !== 'sugerencias';

  return (
    <div style={{ display: 'flex', flexDirection: 'column', gap: 20, maxWidth: 1300, margin: '0 auto' }}>

      {/* Encabezado */}
      <div style={{ display: 'flex', justifyContent: 'space-between', alignItems: 'flex-start', flexWrap: 'wrap', gap: 12 }}>
        <div>
          <h1 style={{ fontSize: 20, fontWeight: 600, color: 'var(--z-text-primary)', margin: 0 }}>
            Informes de Localización
          </h1>
          <p style={{ fontSize: 13, color: 'var(--z-text-muted)', margin: '4px 0 0' }}>
            Cobertura, frescura y rankings de localización por líder y cartera
          </p>
        </div>

        <div style={{ display: 'flex', alignItems: 'center', gap: 10, flexWrap: 'wrap' }}>
          {esAdmin && !problema && (
            <button
              onClick={() => recalcular.mutate()}
              disabled={recalcular.isPending || !!e?.calculando}
              title="Calcula un corte nuevo con los datos de este momento (≈1 minuto, en segundo plano)"
              style={{
                padding: '6px 14px', borderRadius: 7, fontSize: 13, fontWeight: 500,
                border: '1px solid var(--z-border)', background: 'transparent', color: 'var(--z-text-muted)',
                cursor: recalcular.isPending || e?.calculando ? 'not-allowed' : 'pointer', whiteSpace: 'nowrap',
              }}
            >
              {e?.calculando ? 'Calculando…' : '↻ Recalcular'}
            </button>
          )}
          <button
            onClick={handleDescargar}
            disabled={descargando || sinDescarga}
            style={{
              display: 'flex', alignItems: 'center', gap: 6,
              padding: '6px 14px', borderRadius: 7, fontSize: 13, fontWeight: 500,
              background: descargando || sinDescarga ? '#94a3b8' : C.naranja,
              color: '#fff', border: 'none',
              cursor: descargando || sinDescarga ? 'not-allowed' : 'pointer',
              opacity: descargando ? 0.7 : 1,
              transition: 'background 0.15s',
              whiteSpace: 'nowrap',
            }}
          >
            <span style={{ fontSize: 14 }}>{descargando ? '⏳' : '↓'}</span>
            {descargando ? 'Generando…' : 'Descargar Informe'}
          </button>
        </div>
      </div>

      {/* Estados del precálculo. Las Sugerencias no dependen de él: su pestaña funciona siempre. */}
      {estado.isLoading ? <Cargando /> : (
        <>
          {/* Banner del corte */}
          {e?.ultimoOk && (
            <div style={{
              display: 'flex', alignItems: 'center', gap: 10, flexWrap: 'wrap',
              fontSize: 12, color: 'var(--z-text-muted)', background: 'var(--z-bg-card)',
              border: '1px solid var(--z-border)', borderRadius: 8, padding: '8px 14px',
            }}>
              <span>
                Localización al <strong style={{ color: 'var(--z-text-primary)' }}>{fmtFechaHora(e.ultimoOk.fechaCorte)}</strong>
                {' · '}Gestión al <strong style={{ color: 'var(--z-text-primary)' }}>{fmtFechaHora(e.ultimoOk.datosGestionAl)}</strong>
              </span>
              <span style={{ opacity: 0.8 }}>
                ({MOTIVO[e.ultimoOk.motivo] ?? e.ultimoOk.motivo}
                {e.ultimoOk.titulares != null ? ` · ${e.ultimoOk.titulares.toLocaleString('es-CO')} titulares en ${e.ultimoOk.pares} pares` : ''})
              </span>
              {e.calculando && <span style={{ color: '#2563eb', fontWeight: 600 }}>· calculando un corte nuevo…</span>}
              {!e.calculando && e.ultimoIntento?.estado === 'ERROR' && e.ultimoIntento.corteID > e.ultimoOk.corteID && (
                <span style={{ color: '#dc2626' }} title={e.ultimoIntento.error ?? undefined}>· el último recálculo falló; se muestra el anterior</span>
              )}
              {recalcular.isSuccess && !e.calculando && <span style={{ color: '#16a34a' }}>· recálculo solicitado</span>}
            </div>
          )}

          {/* Tabs */}
          {/* Subrayado animado: crece desde el centro al hover; el tab activo queda subrayado completo. */}
          <style>{`
            .z-tab { position: relative; transition: color 0.2s ease; }
            .z-tab::after {
              content: '';
              position: absolute;
              left: 50%; right: 50%; bottom: -1px; height: 2px;
              background: var(--z-brand);
              transition: left 0.22s ease, right 0.22s ease;
            }
            .z-tab:hover { color: var(--z-brand); }
            .z-tab:hover::after { left: 14px; right: 14px; }
            .z-tab-activa::after,
            .z-tab-activa:hover::after { left: 0; right: 0; }
          `}</style>
          <div style={{ display: 'flex', gap: 2, borderBottom: '1px solid var(--z-border)', flexWrap: 'wrap' }}>
            {TABS.map(t => (
              <button
                key={t.id}
                onClick={() => setTabActiva(t.id)}
                className={`z-tab${tabActiva === t.id ? ' z-tab-activa' : ''}`}
                style={{
                  padding: '9px 20px',
                  fontSize: 13,
                  fontWeight: tabActiva === t.id ? 600 : 400,
                  color: tabActiva === t.id ? 'var(--z-brand)' : 'var(--z-text-muted)',
                  background: 'none',
                  border: 'none',
                  cursor: 'pointer',
                  marginBottom: -1,
                }}
              >
                {t.label}
              </button>
            ))}
          </div>

          {/* Contenido */}
          {tabActiva === 'sugerencias' ? (
            <TabSugerencias alcance={alcance} onAlcance={setAlcance} onRegisterDownload={handleRegisterDownload} />
          ) : problema?.tipo === 'sin-tablas' ? (
            <AvisoEstado color="#dc2626" titulo="El módulo todavía no está instalado en la base de datos">
              {problema.mensaje || 'Falta aplicar docs/sql/migrations/2026-09-26_informes_localizacion.sql.'}
              {' '}Un administrador de la base de datos debe ejecutar ese script; mientras tanto estas pestañas no tienen datos.
              {' '}Las Sugerencias sí funcionan: se calculan en línea.
            </AvisoEstado>
          ) : estado.isError || !e ? <ErrorMsg /> : !e.ultimoOk ? (
            <AvisoEstado color="#2563eb" titulo="Calculando el primer corte de localización">
              {e.calculando
                ? `En curso desde ${fmtFechaHora(e.inicioEnCurso)}. Tarda alrededor de un minuto; esta página se actualiza sola.`
                : e.ultimoIntento?.estado === 'ERROR'
                  ? `El último intento falló (${e.ultimoIntento.error ?? 'sin detalle'}). Se reintenta automáticamente en unos minutos${esAdmin ? ' o con "Recalcular"' : ''}.`
                  : 'Todavía no hay un corte calculado. Se está pidiendo uno; esta página se actualiza sola.'}
            </AvisoEstado>
          ) : (
            <>
              {tabActiva === 'resumen'   && <TabResumen   alcance={alcance} onAlcance={setAlcance} onRegisterDownload={handleRegisterDownload} />}
              {tabActiva === 'cobertura' && <TabCobertura alcance={alcance} onAlcance={setAlcance} onRegisterDownload={handleRegisterDownload} />}
              {tabActiva === 'frescura'  && <TabFrescura  alcance={alcance} onAlcance={setAlcance} onRegisterDownload={handleRegisterDownload} />}
              {tabActiva === 'rankings'  && <TabRankings  onRegisterDownload={handleRegisterDownload} />}
            </>
          )}
        </>
      )}

      {/* Modal de selección de columnas para el PDF */}
      {modalCols && (
        <ModalColumnas
          columnas={modalCols.columnas}
          generando={descargando}
          onConfirm={async (idx) => {
            setDescargando(true);
            try { await modalCols.generar(idx); }
            finally { setDescargando(false); setModalCols(null); }
          }}
          onClose={() => setModalCols(null)}
        />
      )}
    </div>
  );
}

function AvisoEstado({ color, titulo, children }: { color: string; titulo: string; children: React.ReactNode }) {
  return (
    <div style={{
      background: 'var(--z-bg-card)', border: '1px solid var(--z-border)', borderLeft: `4px solid ${color}`,
      borderRadius: 10, padding: '20px 22px', display: 'flex', flexDirection: 'column', gap: 6,
    }}>
      <p style={{ margin: 0, fontSize: 15, fontWeight: 600, color: 'var(--z-text-primary)' }}>{titulo}</p>
      <p style={{ margin: 0, fontSize: 13, color: 'var(--z-text-muted)', lineHeight: 1.5 }}>{children}</p>
    </div>
  );
}
