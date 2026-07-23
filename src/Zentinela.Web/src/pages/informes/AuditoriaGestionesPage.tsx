import { useState, useEffect, useLayoutEffect, useRef, Fragment } from 'react';
import { useQuery, useQueryClient } from '@tanstack/react-query';
import {
  analizarAuditoria, obtenerOpcionesAuditoria, descargarAuditoriaCompleta, estimarCostoAuditoria,
  obtenerReportesGenerados,
} from '../../api/auditoriaGestionesApi';
import type {
  IncongruenciaFila, TipoIncongruencia, Severidad,
} from '../../types/auditoriaGestiones';

// ── Paleta (alineada con Informes de Virtualidad) ──────────────────────────────
const C = {
  brand:      '#F54927',
  brandLight: '#FEF0EC',
  success:    '#16a34a',
  warning:    '#d97706',
  danger:     '#dc2626',
  gray:       '#64748b',
  auditoria:  '#0891b2',   // color del sub-módulo
};

// Etiqueta legible por tipo de hallazgo.
const TIPO_LABEL: Record<TipoIncongruencia, string> = {
  'perfil-gestion':    'Perfil ↔ Gestión',
  'promesa-sin-valor': 'Promesa sin valor',
  'valor-sin-fecha':   'Valor sin fecha',
  'valor-bajo':        'Valor < 5 dígitos',
};

// Aclaración al pasar el mouse cuando el asesor no se pudo resolver (dos motivos distintos).
const ASESOR_TIP: Record<string, string> = {
  'Gestión sin agente':   'La gestión no tiene un agente asociado en el histórico.',
  'No se encuentra agente': 'El agente tiene un ID registrado, pero puede que no esté en la base de datos de Recursos Humanos.',
};

// Metadatos por nivel de severidad (etiqueta + descripción + color).
const SEV_META: Record<Severidad, { label: string; desc: string; color: string }> = {
  critico:  { label: 'Crítico',           desc: 'Inconsistencia perfil ↔ gestión (IA)',            color: '#dc2626' },  // rojo
  medio:    { label: 'Medio',             desc: 'Promesa incompleta (fecha sin valor o viceversa)', color: '#d97706' },  // ámbar
  bajo:     { label: 'Bajo',              desc: 'Valor de promesa con menos de 5 dígitos',          color: '#0891b2' },  // cian
  revision: { label: 'Requiere revisión', desc: 'Es ambiguo, la IA no pudo concluir; revisar manualmente', color: '#6366f1' },  // índigo
};

// Severidad robusta: usa la del backend y, si llega un valor desconocido, la infiere del
// tipo (perfil ↔ gestión siempre es crítico; valor < 5 dígitos es bajo; el resto, medio).
function severidadDe(it: IncongruenciaFila): Severidad {
  if (it.severidad === 'critico' || it.severidad === 'medio' || it.severidad === 'bajo' || it.severidad === 'revision') return it.severidad;
  if (it.tipo === 'perfil-gestion') return 'critico';
  if (it.tipo === 'valor-bajo')     return 'bajo';
  return 'medio';
}

// Nombre en Title Case (la BD los trae en MAYÚSCULAS → mucha carga visual).
const tituloCase = (s: string) =>
  s.toLowerCase().split(' ').map(w => (w ? w[0].toUpperCase() + w.slice(1) : w)).join(' ');

// Formateo de pesos colombianos (ej. $ 859,88).
const COP = new Intl.NumberFormat('es-CO', {
  style: 'currency', currency: 'COP', minimumFractionDigits: 2, maximumFractionDigits: 2,
});

// Fecha local en formato yyyy-mm-dd (sin desfase UTC).
const fechaISO = (d: Date) =>
  `${d.getFullYear()}-${String(d.getMonth() + 1).padStart(2, '0')}-${String(d.getDate()).padStart(2, '0')}`;

// Fecha corta con mes abreviado (ej. "06 jul 2026"), para mejor escaneo visual que "06/07/2026".
const MESES_ABREV = ['ene', 'feb', 'mar', 'abr', 'may', 'jun', 'jul', 'ago', 'sep', 'oct', 'nov', 'dic'];
const fechaCorta = (iso: string) => {
  const [y, m, d] = iso.split('-');
  return `${d} ${MESES_ABREV[Number(m) - 1]} ${y}`;
};

// Persistencia en sessionStorage de la auditoría activa (cartera + día + consulta disparada),
// para que al cambiar de módulo y volver el dashboard siga en su sitio (o esperando la carga).
// Sobrevive a la navegación entre módulos e incluso a F5 dentro de la pestaña.
const CLAVE_ACTIVA = 'auditoria-gestiones-activa';
type AuditoriaActiva = { carteraID: number | null; dia: string; consulta: { carteraID: number; dia: string } | null };
const leerActiva = (): AuditoriaActiva => {
  try {
    const raw = sessionStorage.getItem(CLAVE_ACTIVA);
    if (raw) return JSON.parse(raw) as AuditoriaActiva;
  } catch { /* ignora JSON inválido */ }
  return { carteraID: null, dia: '', consulta: null };
};

// ── Componentes base (mismo patrón que InformesVirtualidadPage) ────────────────

// Card y etiqueta reutilizables del dashboard.
const cardStyle: React.CSSProperties = {
  background: 'var(--z-bg-card)', border: '1px solid var(--z-border)', borderRadius: 8, padding: 16,
};
const cardLabel: React.CSSProperties = {
  fontSize: 11, fontWeight: 600, color: C.gray, textTransform: 'uppercase',
  letterSpacing: '0.04em', margin: 0,
};

function ErrorPanel({ mensaje }: { mensaje: string }) {
  return (
    <div style={{
      background: '#fef2f2', border: `1px solid ${C.danger}30`, borderLeft: `4px solid ${C.danger}`,
      borderRadius: 8, padding: '12px 16px', color: C.danger, fontSize: 13,
      display: 'flex', alignItems: 'center', gap: 8,
    }}>
      <span>⚠</span> {mensaje}
    </div>
  );
}

/// Aviso ámbar (no es un error: el informe es válido, pero está incompleto).
function AvisoPanel({ mensaje }: { mensaje: string }) {
  return (
    <div style={{
      background: '#fffbeb', border: '1px solid #f59e0b30', borderLeft: '4px solid #f59e0b',
      borderRadius: 8, padding: '12px 16px', color: '#92400e', fontSize: 13,
      display: 'flex', alignItems: 'center', gap: 8,
    }}>
      <span>⚠</span> {mensaje}
    </div>
  );
}

const thStyle: React.CSSProperties = {
  padding: '8px 12px', textAlign: 'left', color: C.gray, fontSize: 11, fontWeight: 600,
  textTransform: 'uppercase', letterSpacing: '0.04em', borderBottom: '2px solid var(--z-border)',
  background: 'var(--z-bg-page)', position: 'sticky', top: 0, zIndex: 1,
};
const tdStyle: React.CSSProperties = { padding: '7px 12px', fontSize: 13, borderBottom: '1px solid var(--z-border)' };

// Badge de severidad (crítico / medio / bajo).
function SeveridadBadge({ severidad }: { severidad: Severidad }) {
  const meta = SEV_META[severidad] ?? SEV_META.bajo;
  return (
    <span style={{
      display: 'inline-block', padding: '2px 8px', borderRadius: 12, fontSize: 11, fontWeight: 600,
      color: meta.color, background: `${meta.color}1a`, whiteSpace: 'nowrap',
    }}>
      {meta.label}
    </span>
  );
}

// ── Página ─────────────────────────────────────────────────────────────────────

const LIMITE_GESTIONES = 5;  // máximo de filas por líder; si hay más, se muestra el aviso de descarga

export function AuditoriaGestionesPage() {
  const [botonHover,    setBotonHover]    = useState<string | null>(null);
  const [filaAbierta,   setFilaAbierta]   = useState<string | null>(null);
  const [liderAbierto,  setLiderAbierto]  = useState<string | null>(null);  // acordeón por líder (contraído por defecto)
  // Estado inicial restaurado desde sessionStorage (auditoría activa antes de cambiar de módulo).
  const [carteraSel,    setCarteraSel]    = useState<number | null>(() => leerActiva().carteraID);  // una sola cartera
  const [menuAbierto,   setMenuAbierto]   = useState(false);
  const [dia,           setDia]           = useState(() => leerActiva().dia);  // un solo día a auditar
  const [consulta,      setConsulta]      = useState<{ carteraID: number; dia: string } | null>(() => leerActiva().consulta);  // dispara la auditoría
  const [errorMsg,      setErrorMsg]      = useState<string | null>(null);  // error de auditoría (no se auto-reintenta al volver)
  const dropdownRef = useRef<HTMLDivElement>(null);
  const queryClient = useQueryClient();

  // Guarda la auditoría activa para que sobreviva al cambio de módulo (o se borre al reiniciar).
  useEffect(() => {
    if (carteraSel == null && !dia && !consulta) sessionStorage.removeItem(CLAVE_ACTIVA);
    else sessionStorage.setItem(CLAVE_ACTIVA, JSON.stringify({ carteraID: carteraSel, dia, consulta }));
  }, [carteraSel, dia, consulta]);

  // En una recarga real (F5/cierre) se limpia: la caché de React Query se pierde y restaurar
  // la consulta re-dispararía la IA (y su costo). El cambio de módulo NO dispara 'beforeunload',
  // así que ese caso sí conserva el dashboard. Un reporte terminado se reabre desde sus chips.
  useEffect(() => {
    const alDescargar = () => sessionStorage.removeItem(CLAVE_ACTIVA);
    window.addEventListener('beforeunload', alDescargar);
    return () => window.removeEventListener('beforeunload', alDescargar);
  }, []);

  // Cerrar el desplegable de carteras al hacer clic fuera de él.
  useEffect(() => {
    if (!menuAbierto) return;
    const alClic = (e: MouseEvent) => {
      if (dropdownRef.current && !dropdownRef.current.contains(e.target as Node)) setMenuAbierto(false);
    };
    document.addEventListener('mousedown', alClic);
    return () => document.removeEventListener('mousedown', alClic);
  }, [menuAbierto]);

  const { data: opciones = [], isLoading: cargandoOpciones } = useQuery({
    queryKey: ['auditoria-opciones'],
    queryFn:  obtenerOpcionesAuditoria,
    staleTime: 10 * 60 * 1000,
  });

  // La auditoría es un useQuery (no useMutation) para que el resultado se cachee y sobreviva
  // a cambios de módulo y a "irse mientras carga". Se dispara al setear `consulta`.
  const analizar = useQuery({
    queryKey: ['auditoria', consulta?.carteraID, consulta?.dia],
    queryFn:  () => analizarAuditoria([{ carteraID: consulta!.carteraID, liderID: null }], consulta!.dia, consulta!.dia),
    enabled:  !!consulta,
    gcTime:   48 * 60 * 60 * 1000,   // conservar en caché 2 días
    staleTime: 48 * 60 * 60 * 1000,  // mismo cartera+día no se re-audita
    retry: false,                    // una auditoría fallida NO se reintenta sola (es costosa)
    refetchOnWindowFocus: false,     // volver a la pestaña no vuelve a auditar
  });

  const informe = analizar.data?.informe;

  // Si la auditoría falla, guardamos el mensaje y DESACTIVAMOS la consulta: así no queda
  // persistida ni se vuelve a disparar sola al cambiar de pestaña/módulo y volver.
  useEffect(() => {
    if (analizar.isError) {
      setErrorMsg(analizar.error instanceof Error ? analizar.error.message : 'No se pudo analizar la cartera.');
      setConsulta(null);
    }
  }, [analizar.isError]);   // eslint-disable-line react-hooks/exhaustive-deps

  // Al cambiar la selección (cartera/día) se limpia el error previo.
  useEffect(() => { setErrorMsg(null); }, [carteraSel, dia]);

  // Reportes ya generados (compartidos, en memoria del servidor). Refresca al generar uno.
  const { data: reportes = [] } = useQuery({
    queryKey: ['auditoria-reportes'],
    queryFn:  obtenerReportesGenerados,
    staleTime: 30 * 1000,
    refetchOnWindowFocus: true,
  });
  useEffect(() => {
    if (analizar.data) queryClient.invalidateQueries({ queryKey: ['auditoria-reportes'] });
  }, [analizar.dataUpdatedAt]);   // eslint-disable-line react-hooks/exhaustive-deps

  // Tope del selector: no se puede elegir un día futuro.
  const hoyISO = fechaISO(new Date());

  // Estimación de costo (COP) tras elegir cartera + día. Usa :countTokens (gratis).
  const { data: estimacion, isFetching: estimando, isError: errorEstimacion } = useQuery({
    queryKey: ['auditoria-estimacion', carteraSel, dia],
    queryFn:  () => estimarCostoAuditoria([{ carteraID: carteraSel!, liderID: null }], dia, dia),
    enabled:  carteraSel != null && !!dia && !informe,
    staleTime: 5 * 60 * 1000,
  });

  // Agrupa las opciones cartera–líder en una opción por cartera (con todos sus líderes).
  const carterasAgrupadas = (() => {
    const map = new Map<number, { carteraID: number; cartera: string; lideres: string[] }>();
    for (const o of opciones) {
      const [cartera, lider] = o.etiqueta.split(' — ');
      const g = map.get(o.carteraID) ?? { carteraID: o.carteraID, cartera, lideres: [] };
      if (o.liderID != null && lider && !g.lideres.includes(lider)) g.lideres.push(lider);
      map.set(o.carteraID, g);
    }
    return [...map.values()];
  })();

  const carteraSeleccionada = carterasAgrupadas.find(g => g.carteraID === carteraSel) ?? null;
  const puedeAnalizar = carteraSel != null && !!dia;
  const hallazgos = (informe?.incongruenciasPorLider ?? []).flatMap(g => g.items);

  // Masonry para "Top palabras": se ubica en la columna cuya BASE (sin esa card) sea más corta.
  // Al medir la base (que la excluye) no hay oscilación. Tipificaciones corta → va en col 2;
  // Tipificaciones larga → va en col 1 (debajo de "Palabra más repetida").
  const base1Ref = useRef<HTMLDivElement>(null);
  const base2Ref = useRef<HTMLDivElement>(null);
  const [topEnCol1, setTopEnCol1] = useState(false);
  useLayoutEffect(() => {
    const medir = () => {
      const h1 = base1Ref.current?.offsetHeight ?? 0;
      const h2 = base2Ref.current?.offsetHeight ?? 0;
      if (h1 && h2) setTopEnCol1(h1 <= h2);
    };
    medir();
    const ro = new ResizeObserver(medir);
    if (base1Ref.current) ro.observe(base1Ref.current);
    if (base2Ref.current) ro.observe(base2Ref.current);
    window.addEventListener('resize', medir);
    return () => { ro.disconnect(); window.removeEventListener('resize', medir); };
  }, [informe]);

  // Card "Top palabras" (reubicable entre columnas según el masonry).
  const cardTopPalabras = informe && (
    <div style={cardStyle}>
      <p style={cardLabel}>Palabras más repetidas en gestiones</p>
      <div style={{ marginTop: 10 }}>
        {informe.topPalabras.map(p => {
          const max = informe.topPalabras[0].conteo;
          return (
            <div key={p.palabra} style={{ display: 'flex', alignItems: 'center', gap: 10, marginBottom: 8 }}>
              <span style={{ width: 84, fontSize: 12, color: 'var(--z-text-primary)' }}>{p.palabra}</span>
              <div style={{ flex: 1, background: 'var(--z-bg-page)', borderRadius: 4, height: 14 }}>
                <div style={{ width: `${(p.conteo / max) * 100}%`, height: '100%', background: C.brand, borderRadius: 4 }} />
              </div>
              <span style={{ width: 28, fontSize: 12, color: C.gray, textAlign: 'right' }}>{p.conteo}</span>
            </div>
          );
        })}
      </div>
    </div>
  );

  const reiniciar = () => {
    setCarteraSel(null);
    setDia('');
    setConsulta(null);
    setErrorMsg(null);
  };

  return (
    <div style={{ display: 'flex', flexDirection: 'column', gap: 20 }}>

      {/* Encabezado estándar */}
      <div style={{ display: 'flex', justifyContent: 'space-between', alignItems: 'flex-start', flexWrap: 'wrap', gap: 12 }}>
        <div>
          <h1 style={{ fontSize: 20, fontWeight: 600, color: 'var(--z-text-primary)', margin: 0 }}>
            Auditoría de Gestiones
          </h1>
          <p style={{ fontSize: 13, color: 'var(--z-text-muted)', margin: '4px 0 0' }}>
            Audita una cartera evaluando la congruencia de sus gestiones (perfil ↔ gestión, promesa) con apoyo de IA.
          </p>
        </div>
        {informe && (
          <div style={{ display: 'flex', alignItems: 'center', gap: 10, flexWrap: 'wrap' }}>
            <button
              onClick={() => informe && descargarAuditoriaCompleta(informe, dia)}
              disabled={hallazgos.length === 0}
              onMouseEnter={() => setBotonHover('descargar')}
              onMouseLeave={() => setBotonHover(p => p === 'descargar' ? null : p)}
              title={hallazgos.length === 0 ? 'No hay hallazgos para descargar' : 'Descargar el informe en Excel'}
              style={{
                display: 'inline-flex', alignItems: 'center', gap: 7,
                padding: '9px 18px', borderRadius: 8, border: `1px solid ${C.brand}`,
                background: hallazgos.length === 0 ? 'var(--z-bg-page)' : (botonHover === 'descargar' ? C.brand : '#fff'),
                color: hallazgos.length === 0 ? C.gray : (botonHover === 'descargar' ? '#fff' : C.brand),
                fontSize: 13, fontWeight: 600,
                cursor: hallazgos.length === 0 ? 'not-allowed' : 'pointer',
                transition: 'background 0.15s ease, color 0.15s ease',
              }}
            >
              <span style={{ fontSize: 14, lineHeight: 1 }}>⭳</span> Descargar
            </button>
            <button
              onClick={reiniciar}
              onMouseEnter={() => setBotonHover('reiniciar')}
              onMouseLeave={() => setBotonHover(p => p === 'reiniciar' ? null : p)}
              style={{
                display: 'inline-flex', alignItems: 'center', gap: 7,
                padding: '9px 18px', borderRadius: 8, border: `1px solid ${C.brand}`,
                background: botonHover === 'reiniciar' ? C.brand : C.brandLight,
                color: botonHover === 'reiniciar' ? '#fff' : C.brand,
                fontSize: 13, fontWeight: 600, cursor: 'pointer',
                transition: 'background 0.15s ease, color 0.15s ease',
              }}
            >
              <span style={{ fontSize: 15, lineHeight: 1 }}>↺</span> Nueva auditoría
            </button>
          </div>
        )}
      </div>

      {/* ── Panel de filtro (carteras + período) ──────────────────────────────── */}
      {!informe && (
        <div style={{ display: 'flex', flexDirection: 'column', gap: 16, maxWidth: 1090 }}>

          <div style={{ display: 'flex', gap: 16, flexWrap: 'wrap', alignItems: 'stretch' }}>

            {/* Columna izquierda — Paso 1 + Paso 2 */}
            <div style={{
              flex: '1 1 340px', maxWidth: 460, display: 'flex', flexDirection: 'column', gap: 16,
              border: '1px solid var(--z-border)', borderRadius: 10, background: 'var(--z-bg-card)', padding: 18,
            }}>
              {/* Paso 1 — Carteras a comparar */}
              <div style={{ display: 'flex', flexDirection: 'column', gap: 6 }}>
                <p style={{ fontSize: 11, fontWeight: 600, color: C.gray, textTransform: 'uppercase', letterSpacing: '0.04em' }}>
                  Paso 1 — Selecciona la cartera a auditar
                </p>
                {cargandoOpciones ? (
                  <p style={{ fontSize: 13, color: C.gray }}>Cargando carteras…</p>
                ) : (
                  <div ref={dropdownRef} style={{ position: 'relative' }}>
                    <button
                      type="button"
                      onClick={() => setMenuAbierto(o => !o)}
                      style={{
                        width: '100%', display: 'flex', alignItems: 'center', justifyContent: 'space-between', gap: 10,
                        padding: '10px 14px', fontSize: 13, borderRadius: 8, textAlign: 'left',
                        border: `1px solid ${menuAbierto ? C.brand : 'var(--z-border)'}`,
                        background: 'var(--z-bg-card)', cursor: 'pointer', outline: 'none',
                        boxShadow: menuAbierto ? `0 0 0 3px ${C.brand}1f` : 'none',
                        transition: 'border-color .15s ease, box-shadow .15s ease',
                      }}
                    >
                      <span style={{
                        color: carteraSeleccionada ? 'var(--z-text-primary)' : C.gray,
                        fontWeight: carteraSeleccionada ? 600 : 400,
                        whiteSpace: 'nowrap', overflow: 'hidden', textOverflow: 'ellipsis',
                      }}>
                        {carteraSeleccionada ? carteraSeleccionada.cartera : 'Selecciona una cartera'}
                      </span>
                      <span style={{ color: C.gray, fontSize: 10, flexShrink: 0, transform: menuAbierto ? 'rotate(180deg)' : 'none', transition: 'transform .15s ease' }}>▼</span>
                    </button>

                    {menuAbierto && (
                      <div style={{
                        position: 'absolute', top: 'calc(100% + 6px)', left: 0, right: 0, zIndex: 20,
                        background: 'var(--z-bg-card)', border: '1px solid var(--z-border)', borderRadius: 8,
                        boxShadow: '0 8px 24px rgba(0,0,0,0.12)', maxHeight: 320, overflowY: 'auto', padding: 4,
                      }}>
                        {carterasAgrupadas.map(g => {
                          const sel = g.carteraID === carteraSel;
                          return (
                            <button
                              key={g.carteraID}
                              type="button"
                              onClick={() => { setCarteraSel(g.carteraID); setMenuAbierto(false); }}
                              onMouseEnter={e => { if (!sel) e.currentTarget.style.background = 'var(--z-bg-page)'; }}
                              onMouseLeave={e => { if (!sel) e.currentTarget.style.background = 'transparent'; }}
                              style={{
                                width: '100%', textAlign: 'left', display: 'flex', flexDirection: 'column', gap: 2,
                                padding: '9px 12px', borderRadius: 6, border: 'none', cursor: 'pointer',
                                background: sel ? C.brandLight : 'transparent', transition: 'background .12s ease',
                              }}
                            >
                              <span style={{ fontSize: 13, fontWeight: 600, color: sel ? C.brand : 'var(--z-text-primary)' }}>{g.cartera}</span>
                              {g.lideres.length > 0 && (
                                <span style={{ fontSize: 11, color: C.gray, lineHeight: 1.35 }}>
                                  {g.lideres.map(tituloCase).join(' · ')}
                                </span>
                              )}
                            </button>
                          );
                        })}
                      </div>
                    )}
                  </div>
                )}
              </div>

              {/* Paso 2 — Día a auditar */}
              <div style={{ display: 'flex', flexDirection: 'column', gap: 6 }}>
                <p style={{ fontSize: 11, fontWeight: 600, color: C.gray, textTransform: 'uppercase', letterSpacing: '0.04em' }}>
                  Paso 2 — Día a auditar (Fecha Última Gestión)
                </p>
                <div style={{ display: 'flex', flexDirection: 'column', gap: 4 }}>
                  <input type="date" value={dia} max={hoyISO} onChange={e => setDia(e.target.value)}
                    style={{ padding: '8px 12px', fontSize: 13, borderRadius: 7, maxWidth: 220, border: '1px solid var(--z-border)', background: 'var(--z-bg-page)', color: 'var(--z-text-primary)', outline: 'none' }} />
                  <span style={{ fontSize: 11, color: C.gray }}>Se audita un solo día (cualquiera hasta hoy).</span>
                </div>
              </div>

              {!puedeAnalizar && !analizar.isFetching && (
                <span style={{ fontSize: 11, color: C.gray }}>Selecciona una cartera y el día a auditar.</span>
              )}
            </div>

            {/* Columna derecha — Costo aproximado + botón (aparece tras elegir cartera + día) */}
            {puedeAnalizar && (
              <div style={{
                flex: '1 1 260px', maxWidth: 300, display: 'flex', flexDirection: 'column', gap: 10,
                border: `1px solid ${C.brand}30`, background: C.brandLight, borderRadius: 10, padding: '16px 18px',
              }}>
                <span style={{ fontSize: 11, fontWeight: 600, color: C.gray, textTransform: 'uppercase', letterSpacing: '0.04em' }}>
                  Costo aproximado de la auditoría con IA
                </span>
                {estimando ? (
                  <span style={{ fontSize: 13, color: C.gray }}>Calculando…</span>
                ) : errorEstimacion ? (
                  <span style={{ fontSize: 13, color: C.danger }}>No se pudo estimar el costo.</span>
                ) : estimacion ? (
                  estimacion.registrosIA === 0 ? (
                    <span style={{ fontSize: 13, color: 'var(--z-text-primary)' }}>
                      Sin gestiones con texto para evaluar con IA en este día → <strong>{COP.format(0)}</strong>.
                    </span>
                  ) : (
                    <>
                      <span style={{ fontSize: 28, fontWeight: 700, color: C.brand, lineHeight: 1.1 }}>
                        {COP.format(estimacion.costoTotal)}
                      </span>
                      <span style={{ fontSize: 12, color: C.gray }}>
                        {estimacion.registrosIA.toLocaleString('es-CO')} gestiones totales
                      </span>
                    </>
                  )
                ) : null}

                {!estimando && (
                  <div style={{ display: 'flex', alignItems: 'center', gap: 10, marginTop: 4 }}>
                    <button
                      onClick={() => { if (carteraSel != null) { setErrorMsg(null); setConsulta({ carteraID: carteraSel, dia }); } }}
                      disabled={analizar.isFetching}
                      style={{
                        flex: 1, padding: '11px 18px', borderRadius: 8, border: 'none',
                        display: 'inline-flex', alignItems: 'center', justifyContent: 'center', gap: 8,
                        background: analizar.isFetching ? 'var(--z-border)' : C.brand,
                        color: '#fff', fontSize: 14, fontWeight: 600,
                        cursor: analizar.isFetching ? 'not-allowed' : 'pointer',
                      }}
                    >
                      {analizar.isFetching ? 'Analizando con IA…' : <>Auditar cartera con IA <span>→</span></>}
                    </button>
                    {analizar.isFetching && (
                      <span aria-label="Cargando" style={{
                        width: 18, height: 18, borderRadius: '50%', display: 'inline-block', flexShrink: 0,
                        border: `2px solid ${C.brand}33`, borderTopColor: C.brand,
                        animation: 'z-spiral-cw 0.7s linear infinite',
                      }} />
                    )}
                  </div>
                )}
              </div>
            )}
          </div>

          {errorMsg && <ErrorPanel mensaje={`No se pudo analizar: ${errorMsg}`} />}

          {/* Reportes ya generados (compartidos entre usuarios, expiran a 48 h): una card por
              cartera, con las fechas disponibles dentro. Una cartera sin fechas no se muestra. */}
          {reportes.length > 0 && (() => {
            const porCartera = new Map<number, { nombre: string; fechas: string[] }>();
            for (const r of reportes) {
              const nombre = carterasAgrupadas.find(g => g.carteraID === r.carteraID)?.cartera ?? `Cartera ${r.carteraID}`;
              const grupo = porCartera.get(r.carteraID) ?? { nombre, fechas: [] };
              grupo.fechas.push(r.dia);
              porCartera.set(r.carteraID, grupo);
            }
            const grupos = [...porCartera.entries()]
              .map(([carteraID, g]) => ({
                // Fechas de cada card: más reciente primero.
                carteraID, nombre: g.nombre, fechas: g.fechas.sort((a, b) => b.localeCompare(a)),
              }))
              // Cards ordenadas alfabéticamente por nombre de cartera.
              .sort((a, b) => a.nombre.localeCompare(b.nombre));

            return (
              <div style={{ display: 'flex', flexDirection: 'column', gap: 6, marginTop: 4 }}>
                <p style={{ fontSize: 11, fontWeight: 600, color: C.gray, textTransform: 'uppercase', letterSpacing: '0.04em' }}>
                  Reportes ya generados (disponibles)
                </p>
                <div style={{ display: 'grid', gridTemplateColumns: 'repeat(auto-fit, minmax(180px, 1fr))', gap: 10 }}>
                  {grupos.map((g, i) => (
                    <div key={g.carteraID} style={{
                      border: '1px solid var(--z-border)', borderRadius: 10, background: 'var(--z-bg-card)',
                      padding: '10px 14px',
                      // La última card siempre arranca en una fila nueva (misma columna normal),
                      // en vez de quedar compartiendo la fila final con la penúltima.
                      ...(i === grupos.length - 1 ? { gridColumnStart: 1 } : {}),
                    }}>
                      <div style={{ display: 'flex', alignItems: 'center', gap: 6 }}>
                        <span style={{
                          display: 'inline-flex', alignItems: 'center', justifyContent: 'center',
                          width: 16, height: 16, borderRadius: '50%', background: '#dcfce7', color: C.success,
                          fontSize: 10, flexShrink: 0,
                        }}>✓</span>
                        <strong style={{ fontSize: 13, color: 'var(--z-text-primary)' }}>{g.nombre}</strong>
                      </div>
                      <div style={{ display: 'flex', flexWrap: 'wrap', gap: 6, marginTop: 8 }}>
                        {g.fechas.map(dia => (
                          <button
                            key={dia}
                            type="button"
                            onClick={() => { setCarteraSel(g.carteraID); setDia(dia); setConsulta({ carteraID: g.carteraID, dia }); }}
                            title="Abrir este reporte (ya generado)"
                            style={{
                              padding: '4px 10px', borderRadius: 20, border: `1px solid ${C.brand}40`,
                              background: '#fff', color: C.brand, fontSize: 11.5, fontWeight: 600, cursor: 'pointer',
                            }}
                          >
                            {fechaCorta(dia)}
                          </button>
                        ))}
                      </div>
                    </div>
                  ))}
                </div>
              </div>
            );
          })()}
        </div>
      )}

      {/* ── Dashboard de resultados ───────────────────────────────────────────── */}
      {informe && (
        <>
          {/* Barra de contexto: cartera + día auditado */}
          <div style={{ display: 'flex', flexWrap: 'wrap', gap: 8 }}>
            {carteraSeleccionada && (
              <span style={{ display: 'inline-flex', alignItems: 'center', gap: 6, padding: '5px 12px', borderRadius: 20, background: C.brandLight, border: `1px solid ${C.brand}30`, color: C.brand, fontSize: 12, fontWeight: 600 }}>
                🗂 {carteraSeleccionada.cartera}
              </span>
            )}
            {dia && (
              <span style={{ display: 'inline-flex', alignItems: 'center', gap: 6, padding: '5px 12px', borderRadius: 20, background: C.brandLight, border: `1px solid ${C.brand}30`, color: C.brand, fontSize: 12, fontWeight: 600 }}>
                📅 {dia.split('-').reverse().join('/')}
              </span>
            )}
          </div>

          {/* Aviso de auditoría incompleta. Dos causas distintas, se nombran por separado:
              · lotes que la IA no alcanzó a responder (fallaron tras el reintento)
              · gestiones que nunca se enviaron por el tope de costo (MaxGestionesIA)
              Sin esto, un informe parcial se leería como si fuera completo. */}
          {(() => {
            const nuncaEnviadas = Math.max(0, informe.totalGestionesConTexto - informe.gestionesEvaluadasIA);
            const sinEvaluar    = informe.gestionesSinEvaluar;
            if (nuncaEnviadas === 0 && sinEvaluar === 0) return null;

            const n = (x: number) => x.toLocaleString('es-CO');
            const auditadas = informe.gestionesEvaluadasIA - sinEvaluar;
            const motivos = [
              sinEvaluar    > 0 && `${n(sinEvaluar)} quedaron sin evaluar porque la IA no respondió`,
              nuncaEnviadas > 0 && `${n(nuncaEnviadas)} no se enviaron por el tope de costo`,
            ].filter(Boolean).join(' y ');

            return (
              <AvisoPanel mensaje={
                `Solo se pudieron auditar ${n(auditadas)} de ${n(informe.totalGestionesConTexto)} gestiones con texto: ${motivos}.`
              } />
            );
          })()}

          {/* ── Distribución de cards estilo "Consistencia y Cartera" ───────────── */}
          <div style={{
            display: 'grid',
            gridTemplateColumns: 'minmax(220px, 0.9fr) minmax(240px, 1.1fr) minmax(360px, 1.8fr)',
            gap: 16, alignItems: 'start',
          }}>

            {/* Columna 1 — severidad + palabra (base medible) + top palabras (masonry) */}
            <div style={{ display: 'flex', flexDirection: 'column', gap: 16 }}>
              <div ref={base1Ref} style={{ display: 'flex', flexDirection: 'column', gap: 16 }}>
              <div style={{ ...cardStyle, padding: '18px 20px' }}>
                <p style={cardLabel}>Hallazgos por severidad</p>
                <div style={{ display: 'flex', flexDirection: 'column', gap: 12, marginTop: 12 }}>
                  {([
                    ['critico',  informe.totalCriticas],
                    ['medio',    informe.totalMedias],
                    ['bajo',     informe.totalBajas],
                    ['revision', informe.totalRevision],
                  ] as [Severidad, number][]).map(([sev, n]) => {
                    const m = SEV_META[sev];
                    return (
                      <div key={sev} style={{ display: 'flex', alignItems: 'center', gap: 10 }}>
                        <span style={{ width: 9, height: 9, borderRadius: '50%', background: m.color, marginTop: 3, flexShrink: 0, alignSelf: 'flex-start' }} />
                        <div style={{ flex: 1 }}>
                          <div style={{ fontSize: 13, fontWeight: 600, color: m.color }}>{m.label}</div>
                          <div style={{ fontSize: 10.5, color: C.gray, lineHeight: 1.35 }}>{m.desc}</div>
                        </div>
                        <span style={{ fontSize: 18, fontWeight: 700, color: m.color }}>{n}</span>
                      </div>
                    );
                  })}
                </div>
              </div>

              <div style={{
                background: C.brandLight, border: `1px solid ${C.brand}30`,
                borderRadius: 8, padding: '14px 20px',
              }}>
                <p style={cardLabel}>Palabra más repetida</p>
                <div style={{ fontSize: 20, fontWeight: 700, color: C.brand, marginTop: 4 }}>“{informe.palabraMasRepetida.palabra}”</div>
                <div style={{ fontSize: 12, color: C.gray, marginTop: 2 }}>{informe.palabraMasRepetida.conteo} apariciones · todas las gestiones</div>

                <div style={{ marginTop: 8, paddingTop: 7, borderTop: `1px solid ${C.brand}30` }}>
                  <p style={{ ...cardLabel, marginBottom: 2 }}>En gestiones con inconsistencias</p>
                  {informe.palabraInconsistencias.palabra ? (
                    <>
                      <div style={{ fontSize: 16, fontWeight: 700, color: C.brand, marginTop: 3 }}>“{informe.palabraInconsistencias.palabra}”</div>
                      <div style={{ fontSize: 12, color: C.gray, marginTop: 2 }}>{informe.palabraInconsistencias.conteo} apariciones</div>
                    </>
                  ) : (
                    <div style={{ fontSize: 12, color: C.gray, marginTop: 3 }}>Sin palabras (gestiones sin texto)</div>
                  )}
                </div>
              </div>
              </div>{/* fin base col 1 */}

              {topEnCol1 && cardTopPalabras}
            </div>

            {/* Columna 2 — % + tipificaciones (base medible) + top palabras (masonry) */}
            <div style={{ display: 'flex', flexDirection: 'column', gap: 16 }}>
              <div ref={base2Ref} style={{ display: 'flex', flexDirection: 'column', gap: 16 }}>
              <div style={cardStyle}>
                <p style={cardLabel}>Gestiones con incongruencias</p>
                <div style={{ display: 'flex', alignItems: 'flex-end', justifyContent: 'center', gap: 28, marginTop: 8 }}>
                  <div style={{ textAlign: 'center' }}>
                    <div style={{ fontSize: 18, fontWeight: 700, color: C.brand }}>{informe.totalCriticas + informe.totalMedias + informe.totalBajas + informe.totalRevision}</div>
                    <div style={{ fontSize: 11, color: C.gray }}>Hallazgos alerta</div>
                  </div>
                  <div style={{ textAlign: 'center' }}>
                    <div style={{ fontSize: 32, fontWeight: 700, color: C.brand, lineHeight: 1.05 }}>
                      {informe.porcentajeIncongruencias.toFixed(1)}%
                    </div>
                    <div style={{ fontSize: 11, color: C.gray }}>Porcentaje</div>
                  </div>
                  <div style={{ textAlign: 'center' }}>
                    <div style={{ fontSize: 18, fontWeight: 700, color: '#C0421C' }}>{informe.totalGestiones}</div>
                    <div style={{ fontSize: 11, color: C.gray }}>Total gestiones</div>
                  </div>
                </div>
              </div>

              <div style={cardStyle}>
                <p style={cardLabel}>Tipificaciones con más incongruencias</p>
                <div style={{ marginTop: 10 }}>
                  {(() => {
                    const ranking = [...informe.tipificacionesConMasIncongruencias]
                      .sort((a, b) => b.incongruencias - a.incongruencias)
                      .slice(0, 5);   // el dashboard solo muestra el top 5; el Excel descargable trae todas
                    const max = ranking[0]?.incongruencias || 1;
                    return ranking.map((t, i) => {
                      const pct = t.total > 0 ? (t.incongruencias / t.total) * 100 : 0;
                      return (
                        <div key={t.tipificacion} style={{ marginBottom: 10 }}>
                          <div style={{ display: 'flex', justifyContent: 'space-between', alignItems: 'baseline', gap: 8, fontSize: 12, marginBottom: 3 }}>
                            <span style={{ fontWeight: i === 0 ? 700 : 500, color: i === 0 ? C.danger : 'var(--z-text-primary)' }}>{i === 0 && '⚠ '}{t.tipificacion}</span>
                            <span style={{ color: C.gray, whiteSpace: 'nowrap' }}>{t.incongruencias}/{t.total} · {pct.toFixed(0)}%</span>
                          </div>
                          <div style={{ background: 'var(--z-bg-page)', borderRadius: 4, height: 8 }}>
                            <div style={{ width: `${(t.incongruencias / max) * 100}%`, height: '100%', background: i === 0 ? C.danger : C.brand, borderRadius: 4 }} />
                          </div>
                        </div>
                      );
                    });
                  })()}
                </div>
              </div>
              </div>{/* fin base col 2 */}

              {!topEnCol1 && cardTopPalabras}
            </div>

            {/* Columna 3 — listado de gestiones por líder */}
            <div style={{ ...cardStyle, alignSelf: 'start', display: 'flex', flexDirection: 'column' }}>
              <p style={cardLabel}>Gestiones en alerta por líder</p>
              <p style={{ fontSize: 11, color: C.gray, margin: '4px 0 0' }}>Clic en un líder para ver sus gestiones · clic en una fila para el detalle</p>
              <div style={{ display: 'flex', flexDirection: 'column', gap: 12, marginTop: 10 }}>
                {informe.incongruenciasPorLider.map(grupo => {
                  const hayMas  = grupo.items.length > LIMITE_GESTIONES;
                  const cartera = grupo.items[0]?.cartera ?? '';
                  const abiertoLider = liderAbierto === grupo.lider;
                  return (
                  <div key={grupo.lider} style={{ border: '1px solid var(--z-border)', borderRadius: 8, overflow: 'hidden' }}>
                    <div
                      onClick={() => setLiderAbierto(abiertoLider ? null : grupo.lider)}
                      style={{ display: 'flex', alignItems: 'center', gap: 8, padding: '9px 14px', background: 'var(--z-bg-page)', borderBottom: abiertoLider ? '1px solid var(--z-border)' : 'none', cursor: 'pointer' }}
                    >
                      <span style={{ width: 4, height: 16, borderRadius: 2, background: C.brand }} />
                      <span style={{ fontSize: 11, color: C.gray, width: 10, flexShrink: 0 }}>{abiertoLider ? '▾' : '▸'}</span>
                      <strong style={{ fontSize: 13, color: 'var(--z-text-primary)', flex: 1 }}>{grupo.lider}{cartera && ` — ${cartera}`}</strong>
                      <span style={{ fontSize: 11, color: C.gray, flexShrink: 0 }}>· {grupo.items.length} hallazgo(s)</span>
                    </div>
                    {abiertoLider && (<>
                    <div style={{ overflowX: 'auto' }}>
                      <table style={{ width: '100%', borderCollapse: 'collapse', minWidth: 540 }}>
                        <thead>
                          <tr>
                            <th style={thStyle}>Identificación</th>
                            <th style={thStyle}>Asesor</th>
                            <th style={thStyle}>Cartera</th>
                            <th style={thStyle}>Tipo</th>
                            <th style={thStyle}>Severidad</th>
                          </tr>
                        </thead>
                        <tbody>
                          {grupo.items.slice(0, LIMITE_GESTIONES).map((it, i) => {
                            const key = `${grupo.lider}-${i}`;
                            const abierta = filaAbierta === key;
                            return (
                              <Fragment key={key}>
                                <tr
                                  onClick={() => setFilaAbierta(abierta ? null : key)}
                                  style={{ cursor: 'pointer', background: abierta ? 'var(--z-bg-page)' : 'transparent' }}
                                >
                                  <td style={tdStyle}>{abierta ? '▾' : '▸'} {it.identificacion}</td>
                                  <td style={tdStyle} title={ASESOR_TIP[it.asesorGestion]}>{it.asesorGestion}</td>
                                  <td style={tdStyle}>{it.cartera}</td>
                                  <td style={tdStyle}>{TIPO_LABEL[it.tipo]}</td>
                                  <td style={tdStyle}><SeveridadBadge severidad={severidadDe(it)} /></td>
                                </tr>
                                {abierta && (
                                  <tr>
                                    <td colSpan={5} style={{ padding: '12px 14px', background: '#f8fafc', borderBottom: '1px solid var(--z-border)' }}>
                                      <div style={{ display: 'flex', flexDirection: 'column', gap: 6, fontSize: 12 }}>
                                        <div><strong style={{ color: C.gray }}>Gestión: </strong><span style={{ color: 'var(--z-text-primary)', display: 'block', maxHeight: 110, overflowY: 'auto', overflowWrap: 'anywhere', whiteSpace: 'normal', marginTop: 2, lineHeight: 1.4 }}>“{it.gestion.replace(/,/g, ' ').replace(/\s+/g, ' ').trim()}”</span></div>
                                        <div style={{ display: 'flex', gap: 18, flexWrap: 'wrap' }}>
                                          <span><strong style={{ color: C.gray }}>Último perfil: </strong>{it.ultimoPerfil}</span>
                                          <span title={ASESOR_TIP[it.asesorGestion]}><strong style={{ color: C.gray }}>Asesor: </strong>{it.asesorGestion}</span>
                                        </div>
                                        <div><strong style={{ color: C.gray }}>Detalle: </strong><span style={{ color: C.danger }}>{it.detalle}</span></div>
                                      </div>
                                    </td>
                                  </tr>
                                )}
                              </Fragment>
                            );
                          })}
                        </tbody>
                      </table>
                    </div>
                    {hayMas && (
                      <div style={{
                        display: 'flex', alignItems: 'center', gap: 7, padding: '8px 14px',
                        borderTop: '1px solid var(--z-border)', background: '#fffbeb',
                        fontSize: 11.5, color: C.warning, fontWeight: 600,
                      }}>
                        <span>⚠</span>
                        Mostrando {LIMITE_GESTIONES} de {grupo.items.length}. Descarga el informe para ver todos.
                      </div>
                    )}
                    </>)}
                  </div>
                  );
                })}
              </div>
            </div>
          </div>
        </>
      )}
    </div>
  );
}
