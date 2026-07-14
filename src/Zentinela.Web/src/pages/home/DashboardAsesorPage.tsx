import { useState, useMemo, Fragment } from 'react';
import { useQuery } from '@tanstack/react-query';
import { useNavigate } from 'react-router-dom';
import { useAuth } from '../../context/AuthContext';
import { obtenerDashboardAsesor } from '../../api/dashboardApi';
import { CargandoSpiral } from '../../components/CargandoSpiral';
import { ThOrdenable } from '../../components/ThOrdenable';
import { useSort } from '../../hooks/useSort';
import type { AcuerdoVigenteDto, DashboardAsesorDto, PagosConfirmadosDto } from '../../types/dashboard';

// ── Selector de período ───────────────────────────────────────────────────────

type MesOffset = 0 | -1 | -2;

const OPCIONES_MES: { value: MesOffset; label: string }[] = [
  { value:  0, label: 'Este mes'      },
  { value: -1, label: 'Mes pasado'    },
  { value: -2, label: '2 meses atrás' },
];

function calcularRangoMes(offset: MesOffset): { desde: string; hasta: string; label: string } {
  const hoy    = new Date();
  const año    = hoy.getFullYear();
  const mes    = hoy.getMonth() + offset; // puede ser negativo → Date lo ajusta

  const primerDia = new Date(año, mes, 1);
  const ultimoDia = offset === 0
    ? hoy                                       // este mes → hasta hoy
    : new Date(año, mes + 1, 0);                // meses pasados → último día del mes

  const fmt = (d: Date) =>
    `${d.getFullYear()}-${String(d.getMonth() + 1).padStart(2, '0')}-${String(d.getDate()).padStart(2, '0')}`;

  const labelMes = primerDia.toLocaleDateString('es-CO', { month: 'long', year: 'numeric' });

  return { desde: fmt(primerDia), hasta: fmt(ultimoDia), label: labelMes };
}

// ── Helpers de formato ────────────────────────────────────────────────────────

function formatearFechaActualizacion(iso: string | null): string {
  if (!iso) return 'Sin datos';
  const fecha = new Date(iso);
  const ahora = new Date();
  const esHoy = fecha.toDateString() === ahora.toDateString();

  if (esHoy) {
    const diffMs = ahora.getTime() - fecha.getTime();
    const diffMin = Math.floor(diffMs / 60000);
    if (diffMin < 60) return `hace ${diffMin} minuto${diffMin !== 1 ? 's' : ''}`;
    const diffHoras = Math.floor(diffMin / 60);
    return `hace ${diffHoras} hora${diffHoras !== 1 ? 's' : ''}`;
  }

  return fecha.toLocaleDateString('es-CO', { day: 'numeric', month: 'long', year: 'numeric' });
}

function formatearPorcentaje(valor: number): string {
  return `${(valor * 100).toFixed(1)}%`;
}

function formatearMoneda(valor: number | null): string {
  if (valor === null) return '—';
  return new Intl.NumberFormat('es-CO', { style: 'currency', currency: 'COP', maximumFractionDigits: 0 }).format(valor);
}

// ── Colores de semáforo ───────────────────────────────────────────────────────

const SEMAFORO: Record<string, { bg: string; border: string; badge: string; label: string }> = {
  rojo:     { bg: '#fff5f5', border: '#fecaca', badge: '#dc2626', label: 'Vencido' },
  amarillo: { bg: '#fffbeb', border: '#fde68a', badge: '#d97706', label: 'Próximo' },
  verde:    { bg: '#f0fdf4', border: '#bbf7d0', badge: '#16a34a', label: 'A tiempo' },
};

// ── Barra de frases motivacionales ────────────────────────────────────────────

interface FraseItem {
  icono: string;
  texto: string;
}

// Umbrales de rendimiento para las frases cualitativas de la banda.
// Calibrados con la mediana real de asesores (jul-2026) para que ~la mitad quede en cada lado.
const UMBRAL_GESTIONES_DIA   = 30;   // productividad: gestiones/día (mediana ≈ 33)
const UMBRAL_CONTACTABILIDAD = 0.35; // 35% (mediana ≈ 34,5%)
const UMBRAL_EFECTIVIDAD     = 0.22; // 22% (mediana ≈ 21,8%)

// Días calendario que abarca el período seleccionado (inclusivo).
function diasDelPeriodo(desde: string, hasta: string): number {
  const d1 = new Date(desde + 'T00:00:00');
  const d2 = new Date(hasta + 'T00:00:00');
  return Math.max(1, Math.round((d2.getTime() - d1.getTime()) / 86_400_000) + 1);
}

// Frases construidas con las métricas propias del asesor — solo se muestran si hay dato real.
function construirFrasesDinamicas(
  data: DashboardAsesorDto | undefined,
  rango: { desde: string; hasta: string; label: string },
): FraseItem[] {
  if (!data) return [];
  const items: FraseItem[] = [];

  if (data.rendimiento.fechaActualizacion) {
    // Todas las frases de rendimiento son cualitativas (alta / a mejorar): el valor exacto
    // ya vive en las cards, así no repetimos información.
    const contactabilidadAlta = data.rendimiento.tasaContactabilidad >= UMBRAL_CONTACTABILIDAD;
    items.push({
      icono: 'connect_without_contact',
      texto: contactabilidadAlta ? 'Tienes alta contactabilidad' : 'Sube tu contactabilidad',
    });

    const efectividadAlta = data.rendimiento.tasaEfectividad >= UMBRAL_EFECTIVIDAD;
    items.push({
      icono: 'heart_check',
      texto: efectividadAlta ? 'Tienes alta efectividad' : 'Sube tu efectividad',
    });

    if (data.rendimiento.totalGestiones > 0) {
      const porDia = data.rendimiento.totalGestiones / diasDelPeriodo(rango.desde, rango.hasta);
      const productividadAlta = porDia >= UMBRAL_GESTIONES_DIA;
      items.push({
        icono: productividadAlta ? 'rocket_launch' : 'trending_up',
        texto: productividadAlta ? 'Tienes alta productividad' : 'Sube el ritmo de productividad',
      });
    }
  }

  if (data.acuerdosVigentes.length > 0) {
    items.push({ icono: 'local_police', texto: `${data.acuerdosVigentes.length} acuerdo${data.acuerdosVigentes.length !== 1 ? 's' : ''} vigente${data.acuerdosVigentes.length !== 1 ? 's' : ''}` });
  }

  return items;
}

// Bolsa de frases motivadoras por nivel de avance de meta (honorarios / metaHonorarios).
// Reemplaza al pool genérico anterior: ahora el mensaje se adapta al desempeño del asesor.
// Algunas frases dependen de la fecha (soloInicioMes / soloCierreMes) y solo se muestran
// cuando el asesor mira "Este mes" (ver construirFrasesNivel).
interface FraseNivel extends FraseItem {
  soloInicioMes?: boolean; // solo primera semana del mes (día 1-7)
  soloCierreMes?: boolean; // solo últimos 5 días del mes
}

type NivelMeta = 1 | 2 | 3 | 4;

const FRASES_POR_NIVEL: Record<NivelMeta, FraseNivel[]> = {
  // Nivel 1 — 0% a 40%: ánimo y arranque
  1: [
    { icono: 'footprint',             texto: 'Sabemos que el camino tiene días retadores.' },
    { icono: 'arrow_forward',         texto: 'Enfoquémonos en el siguiente cliente.' },
    { icono: 'wb_sunny',              texto: 'Cada día es una nueva oportunidad para empezar.' },
    { icono: 'schedule',              texto: 'El mes sigue abierto y hay tiempo.', soloInicioMes: true },
    { icono: 'directions_walk',       texto: 'Vamos paso a paso.' },
  ],
  // Nivel 2 — 40% a 60%: reconocimiento del esfuerzo
  2: [
    { icono: 'timeline',              texto: 'Ya recorriste la mitad del camino.' },
    { icono: 'fitness_center',        texto: 'Has hecho un gran esfuerzo.' },
    { icono: 'workspace_premium',     texto: 'Confía en tu talento.' },
    { icono: 'trending_up',           texto: 'Tu constancia te ha traído hasta aquí.' },
    { icono: 'donut_large',           texto: 'Buen progreso hasta ahora.' },
  ],
  // Nivel 3 — 60% a 80%: impulso final
  3: [
    { icono: 'flag',                  texto: '¡Estás muy cerca!' },
    { icono: 'speed',                 texto: 'Mantén ese gran ritmo hoy.' },
    { icono: 'verified',              texto: 'Asegura tu meta.' },
    { icono: 'insights',              texto: 'Tu esfuerzo se nota en cada porcentaje.' },
    { icono: 'sprint',                texto: 'Falta el último empujón, ¡sigue adelante que ya casi saboreas el éxito!' },
    { icono: 'center_focus_strong',   texto: 'Mantén el enfoque y hazlo tuyo.' },
  ],
  // Nivel 4 — 80% a 100%+: celebración
  4: [
    { icono: 'emoji_events',          texto: '¡Qué orgullo ver tu tablero hoy!' },
    { icono: 'volunteer_activism',    texto: 'Gracias por tu entrega.' },
    { icono: 'military_tech',         texto: 'Este resultado es fruto de tu talento y dedicación.' },
    { icono: 'workspace_premium',     texto: 'Has demostrado de qué estás hecho.' },
    { icono: 'celebration',           texto: '¡A celebrar y a disfrutar el cierre de mes!', soloCierreMes: true },
  ],
};

// Traduce el % de avance de meta (0-1+) al nivel de la bolsa de frases.
// Cortes: ≤40% → 1, ≤60% → 2, ≤80% → 3, >80% (incluye meta superada) → 4.
function calcularNivelMeta(pct: number): NivelMeta {
  if (pct <= 0.40) return 1;
  if (pct <= 0.60) return 2;
  if (pct <= 0.80) return 3;
  return 4;
}

// Selecciona las frases del nivel correspondiente al avance de meta del asesor.
// Las frases condicionadas por fecha solo se incluyen si el período visible es "Este mes"
// (mesOffset === 0), porque "inicio/cierre de mes" se mide contra la fecha de hoy.
function construirFrasesNivel(data: DashboardAsesorDto | undefined, mesOffset: MesOffset): FraseItem[] {
  if (!data) return [];
  const { totalHonorarios, metaHonorarios } = data.pagosConfirmados;
  // Sin meta válida no hay % → tratamos como nivel 1 (arranque).
  const pct   = metaHonorarios > 0 ? totalHonorarios / metaHonorarios : 0;
  const nivel = calcularNivelMeta(pct);

  const esEsteMes  = mesOffset === 0;
  const hoy        = new Date();
  const diaDelMes  = hoy.getDate();
  const ultimoDia  = new Date(hoy.getFullYear(), hoy.getMonth() + 1, 0).getDate();
  const esInicioMes = esEsteMes && diaDelMes <= 7;                 // primera semana
  const esCierreMes = esEsteMes && (ultimoDia - diaDelMes) <= 5;   // últimos 5 días

  return FRASES_POR_NIVEL[nivel]
    .filter(f => {
      if (f.soloInicioMes) return esInicioMes;
      if (f.soloCierreMes) return esCierreMes;
      return true;
    })
    .map(({ icono, texto }) => ({ icono, texto }));
}

function hashTexto(texto: string): number {
  let hash = 0;
  for (let i = 0; i < texto.length; i++) {
    hash = (hash * 31 + texto.charCodeAt(i)) >>> 0;
  }
  return hash;
}

// Generador determinístico (mulberry32) — misma semilla siempre da la misma secuencia,
// para que cada asesor vea siempre el mismo orden pero distinto al de sus compañeros.
function crearGeneradorConSemilla(semilla: number) {
  let estado = semilla;
  return () => {
    estado |= 0;
    estado = (estado + 0x6D2B79F5) | 0;
    let t = Math.imul(estado ^ (estado >>> 15), 1 | estado);
    t = (t + Math.imul(t ^ (t >>> 7), 61 | t)) ^ t;
    return ((t ^ (t >>> 14)) >>> 0) / 4294967296;
  };
}

function mezclarConSemilla<T>(arr: T[], semilla: number): T[] {
  const copia = [...arr];
  const random = crearGeneradorConSemilla(semilla);
  for (let i = copia.length - 1; i > 0; i--) {
    const j = Math.floor(random() * (i + 1));
    [copia[i], copia[j]] = [copia[j], copia[i]];
  }
  return copia;
}

function BarraFrasesMotivacionales({ semillaTexto, frasesDinamicas, frasesNivel }: { semillaTexto: string; frasesDinamicas: FraseItem[]; frasesNivel: FraseItem[] }) {
  const frases = useMemo(
    () => mezclarConSemilla([...frasesDinamicas, ...frasesNivel], hashTexto(semillaTexto || 'zentinela')),
    [semillaTexto, frasesDinamicas, frasesNivel],
  );

  const [pausado, setPausado] = useState(false);

  // Pista duplicada: al recorrer el 50% del ancho total vuelve a verse igual → loop continuo sin salto.
  const pista = [...frases, ...frases];

  return (
    <div
      onMouseEnter={() => setPausado(true)}
      onMouseLeave={() => setPausado(false)}
      style={{
        background: 'var(--z-bg-card)',
        borderTop: '2px solid var(--z-brand)', borderBottom: '2px solid var(--z-brand)',
        borderRadius: '2px', padding: '8px 0',
        boxShadow: '0 1px 3px rgba(0,0,0,0.06), 0 1px 2px rgba(0,0,0,0.04)',
        overflow: 'hidden',
        marginLeft: '-32px', marginRight: '-32px', marginTop: '-22px', marginBottom: '24px',
      }}
    >
      <style>{`
        @keyframes zentinelaFrasesLectura {
          from { transform: translateX(0%); }
          to   { transform: translateX(-50%); }
        }
      `}</style>
      <div style={{
        display: 'flex', width: 'max-content',
        animationName: 'zentinelaFrasesLectura',
        animationDuration: '62s',
        animationTimingFunction: 'linear',
        animationIterationCount: 'infinite',
        animationPlayState: pausado ? 'paused' : 'running',
      }}>
        {pista.map((item, i) => (
          <div key={`${item.texto}-${i}`} style={{
            display: 'flex', alignItems: 'center', gap: '8px',
            padding: '0 28px', borderRight: '0.5px solid var(--z-border)',
            whiteSpace: 'nowrap',
          }}>
            <span className="material-symbols-outlined" style={{ fontSize: '20px', color: 'var(--z-brand)' }}>
              {item.icono}
            </span>
            <span style={{ fontSize: '16px', fontWeight: 500, color: 'var(--z-brand)' }}>
              {item.texto}
            </span>
          </div>
        ))}
      </div>
    </div>
  );
}

// ── Sub-componentes ───────────────────────────────────────────────────────────

function TablaAcuerdos({ acuerdos }: { acuerdos: AcuerdoVigenteDto[] }) {
  const { items, sort, toggle } = useSort<AcuerdoVigenteDto>(
    acuerdos,
    { key: 'fechaPromesa', dir: 'asc' },
    (a, k) => (a as unknown as Record<string, string | number | null>)[k],
  );

  return (
    <div style={{
      overflowX: 'auto',
      overflowY: 'auto',
      maxHeight: '344px',
      scrollbarWidth: 'thin',
      scrollbarColor: 'var(--z-border) transparent',
    }}>
    <table style={{ width: '100%', borderCollapse: 'collapse' }}>
      <thead style={{ position: 'sticky', top: 0, zIndex: 1 }}>
        <tr style={{ background: 'var(--z-bg-card)' }}>
          <ThOrdenable label="Titular"       sortKey="nombreTitular" sort={sort} onSort={toggle} />
          <ThOrdenable label="Tipificación"  sortKey="ultimoPerfil"  sort={sort} onSort={toggle} />
          <ThOrdenable label="Fecha promesa" sortKey="fechaPromesa"  sort={sort} onSort={toggle} />
          <ThOrdenable label="Promesa"       sortKey="valorPromesa"  sort={sort} onSort={toggle} align="right" />
          <ThOrdenable label="Saldo"         sortKey="saldoTotal"    sort={sort} onSort={toggle} align="right" />
          <ThOrdenable label="Estado"        sortKey="semaforo"      sort={sort} onSort={toggle} align="center" />
        </tr>
      </thead>
      <tbody>
        {items.map((a, i) => (
          <Fragment key={`${a.titularID}`}>
            {/* Separador de 6px solo ENTRE registros: el primero queda pegado al encabezado */}
            {i > 0 && (
              <tr aria-hidden="true">
                <td colSpan={6} style={{ height: '6px', padding: 0, background: 'var(--z-bg-card)' }} />
              </tr>
            )}
            <FilaAcuerdo acuerdo={a} esPrimera={i === 0} />
          </Fragment>
        ))}
      </tbody>
    </table>
    </div>
  );
}

function FilaAcuerdo({ acuerdo, esPrimera }: { acuerdo: AcuerdoVigenteDto; esPrimera?: boolean }) {
  const color = SEMAFORO[acuerdo.semaforo] ?? SEMAFORO.verde;

  // Solo borde superior e inferior en cada celda; junto con el border-spacing de la tabla,
  // las filas quedan separadas (sin recuadro en los extremos).
  const bordeV   = `0.5px solid ${color.border}`;
  const bordeTop = esPrimera ? `2px solid var(--z-brand)` : bordeV; // acento en la fila más próxima
  const celda    = { padding: '10px 14px', fontSize: '13px', borderTop: bordeTop, borderBottom: bordeV };

  return (
    <tr style={{ background: color.bg }}>
      <td style={{ ...celda, color: 'var(--z-text-primary)' }}>
        {acuerdo.nombreTitular}
        <span style={{ fontSize: '11px', color: 'var(--z-text-muted)', marginLeft: '6px' }}>
          {acuerdo.titularID}
        </span>
      </td>
      <td style={{ ...celda, color: 'var(--z-text-primary)' }}>
        {acuerdo.ultimoPerfil}
      </td>
      <td style={{ ...celda, color: 'var(--z-text-primary)' }}>
        {new Date(acuerdo.fechaPromesa + 'T00:00:00').toLocaleDateString('es-CO', { day: 'numeric', month: 'short', year: 'numeric' })}
      </td>
      <td style={{ ...celda, color: 'var(--z-text-primary)', textAlign: 'right' }}>
        {formatearMoneda(acuerdo.valorPromesa)}
      </td>
      <td style={{ ...celda, color: 'var(--z-text-muted)', textAlign: 'right' }}>
        {formatearMoneda(acuerdo.saldoTotal)}
      </td>
      <td style={{ ...celda, textAlign: 'center' }}>
        <span style={{
          display: 'inline-block',
          padding: '2px 8px',
          borderRadius: '100px',
          fontSize: '11px',
          fontWeight: 600,
          background: color.badge,
          color: '#fff',
        }}>
          {color.label}
        </span>
      </td>
    </tr>
  );
}

function TarjetaMetrica({ label, valor, sub, valorSize = 28 }: { label: string; valor: string; sub?: string; valorSize?: number }) {
  return (
    <div style={{
      background: 'var(--z-bg-card)',
      border: '0.5px solid var(--z-border)',
      borderRadius: '8px',
      padding: '20px',
      flex: '1 1 140px',
      minWidth: '140px',
      boxShadow: '0 1px 3px rgba(0,0,0,0.06), 0 1px 2px rgba(0,0,0,0.04)',
    }}>
      <p style={{ fontSize: '12px', color: 'var(--z-text-primary)', marginBottom: '8px' }}>{label}</p>
      <p style={{ fontSize: `${valorSize}px`, fontWeight: 700, color: 'var(--z-brand)', lineHeight: 1, wordBreak: 'break-word' }}>{valor}</p>
      {sub && <p style={{ fontSize: '11px', color: 'var(--z-text-muted)', marginTop: '6px' }}>{sub}</p>}
    </div>
  );
}

// ── Ring de honorarios ────────────────────────────────────────────────────────

function RingHonorarios({ actual, meta }: { actual: number; meta: number }) {
  const r = 40;
  const circum = 2 * Math.PI * r;
  const pct = meta > 0 ? Math.min(actual / meta, 1) : 0;
  const dash = pct * circum;
  const pctLabel = meta > 0 ? `${(pct * 100).toFixed(0)}%` : '—';

  return (
    <div style={{ display: 'flex', flexDirection: 'column', alignItems: 'center', gap: '8px' }}>
      <svg viewBox="0 0 96 96" width="140" height="140">
        {/* Fondo */}
        <circle cx="48" cy="48" r={r} fill="none" stroke="var(--z-border)" strokeWidth="8" />
        {/* Progreso */}
        <circle
          cx="48" cy="48" r={r} fill="none"
          stroke="var(--z-brand)"
          strokeWidth="8"
          strokeDasharray={`${dash} ${circum}`}
          strokeLinecap="round"
          transform="rotate(-90 48 48)"
          style={{ transition: 'stroke-dasharray 0.4s ease' }}
        />
        <text x="48" y="43" textAnchor="middle" fontSize="14" fontWeight="700"
          fill="var(--z-text-primary)" fontFamily="inherit">
          {pctLabel}
        </text>
        <text x="48" y="57" textAnchor="middle" fontSize="9"
          fill="var(--z-text-muted)" fontFamily="inherit">
          de meta
        </text>
      </svg>
      <p style={{ fontSize: '11px', color: 'var(--z-text-muted)', textAlign: 'center', lineHeight: 1.4 }}>
        Honorarios<br />
        <span style={{ fontWeight: 600, color: 'var(--z-text-primary)' }}>
          {formatearMoneda(actual)}
        </span>
        {' / '}{formatearMoneda(meta)}
      </p>
    </div>
  );
}

// ── Sección Pagos Confirmados ─────────────────────────────────────────────────

function SeccionPagosConfirmados({ pagos, isLoading }: { pagos?: PagosConfirmadosDto; isLoading: boolean }) {
  const navigate = useNavigate();
  return (
    <section>
      <div style={{ display: 'flex', alignItems: 'baseline', justifyContent: 'space-between', marginBottom: '12px' }}>
        <div>
          <h2 style={{ fontSize: '13px', fontWeight: 600, color: 'var(--z-text-primary)', marginBottom: '2px' }}>
            Pagos confirmados
          </h2>
          <p style={{ fontSize: '12px', color: 'var(--z-text-muted)' }}>
            Actualizado última vez:{' '}
            <span style={{ fontWeight: 500, color: 'var(--z-text-primary)' }}>
              {isLoading ? '...' : formatearFechaActualizacion(pagos?.fechaActualizacion ?? null)}
            </span>
          </p>
        </div>
        <button
          onClick={() => navigate('/home/pagos')}
          style={{
            background: 'none',
            border: '0.5px solid var(--z-border)',
            borderRadius: '6px',
            padding: '5px 12px',
            fontSize: '12px',
            color: 'var(--z-brand)',
            cursor: 'pointer',
            fontWeight: 500,
          }}
        >
          Ver detalles →
        </button>
      </div>

      {!isLoading && pagos && (
        <div style={{
          background: 'var(--z-bg-card)',
          border: '0.5px solid var(--z-border)',
          borderRadius: '8px',
          padding: '24px',
          display: 'flex',
          gap: '24px',
          alignItems: 'center',
          flexWrap: 'wrap',
          boxShadow: '0 1px 3px rgba(0,0,0,0.06), 0 1px 2px rgba(0,0,0,0.04)',
        }}>
          {/* Ring principal */}
          <RingHonorarios actual={pagos.totalHonorarios} meta={pagos.metaHonorarios} />

          {/* Separador */}
          <div style={{ width: '1px', background: 'var(--z-border)', alignSelf: 'stretch', minHeight: '80px' }} />

          {/* Tarjetas métricas */}
          <div style={{ display: 'flex', gap: '12px', flexWrap: 'wrap', flex: 1 }}>
            <TarjetaMetrica
              label="Recaudo"
              valor={formatearMoneda(pagos.totalRecaudo)}
              valorSize={18}
            />
            <TarjetaMetrica
              label="Honorarios"
              valor={formatearMoneda(pagos.totalHonorarios)}
              valorSize={18}
            />
            <TarjetaMetrica
              label="Efectividad / gestiones"
              valor={`${(pagos.porcentajeEfectividad * 100).toFixed(1)}%`}
              sub="Acuerdos y abonos / Total gestiones"
            />
          </div>
        </div>
      )}

      {!isLoading && !pagos && (
        <div style={{
          background: 'var(--z-bg-card)',
          border: '0.5px solid var(--z-border)',
          borderRadius: '8px',
          padding: '32px',
          textAlign: 'center',
          fontSize: '13px',
          color: 'var(--z-text-muted)',
          boxShadow: '0 1px 3px rgba(0,0,0,0.06), 0 1px 2px rgba(0,0,0,0.04)',
        }}>
          Sin datos de pagos disponibles.
        </div>
      )}
    </section>
  );
}

// ── Componente principal ──────────────────────────────────────────────────────

export function DashboardAsesorPage() {
  const { usuario } = useAuth();
  const [mesOffset, setMesOffset] = useState<MesOffset>(0);

  const rango = useMemo(() => calcularRangoMes(mesOffset), [mesOffset]);

  const { data, isLoading, isError, error } = useQuery({
    queryKey: ['dashboard-asesor', rango.desde, rango.hasta],
    queryFn: () => obtenerDashboardAsesor(rango.desde, rango.hasta),
    staleTime: 5 * 60 * 1000,
    retry: false,
  });

  const frasesDinamicas = useMemo(() => construirFrasesDinamicas(data, rango), [data, rango]);
  const frasesNivel     = useMemo(() => construirFrasesNivel(data, mesOffset), [data, mesOffset]);

  return (
    <>
      {/* Barra de frases motivacionales — fuera de la columna de contenido para
          ocupar todo el ancho disponible, no solo los 960px del resto (igual que
          BarraDestacadosLider en el Dashboard Director). */}
      <BarraFrasesMotivacionales
        semillaTexto={usuario?.email ?? usuario?.nombreCompleto ?? ''}
        frasesDinamicas={frasesDinamicas}
        frasesNivel={frasesNivel}
      />

    <div style={{ display: 'flex', flexDirection: 'column', gap: '28px', maxWidth: '960px' }}>

      {/* Encabezado */}
      <div style={{ display: 'flex', justifyContent: 'space-between', alignItems: 'flex-start', flexWrap: 'wrap', gap: 12 }}>
        <div>
          <h1 style={{ fontSize: 20, fontWeight: 600, color: 'var(--z-text-primary)', margin: 0 }}>
            Hola, {usuario?.nombreCompleto}
          </h1>
          <p style={{ fontSize: 13, color: 'var(--z-text-muted)', margin: '4px 0 0' }}>
            {isLoading ? 'Cargando...'
              : data?.nombreSupervisor
                ? `Supervisor: ${data.nombreSupervisor}`
                : 'Sin supervisor asignado'}
          </p>
        </div>
      </div>

      {isError && (
        <div style={{
          background: '#fff5f5', border: '0.5px solid #fecaca',
          borderRadius: '8px', padding: '14px 16px',
          fontSize: '13px', color: '#dc2626',
        }}>
          {(error as any)?.response?.data?.mensaje ?? 'No se pudo cargar el dashboard. Intenta de nuevo.'}
        </div>
      )}

      {/* ── Acuerdos vigentes ─────────────────────────────────────────────── */}
      <section>
        <div style={{ display: 'flex', alignItems: 'baseline', gap: '10px', marginBottom: '12px' }}>
          <h2 style={{ fontSize: '13px', fontWeight: 600, color: 'var(--z-text-primary)' }}>
            Acuerdos de pago vigentes
          </h2>
          {data && (
            <span style={{ fontSize: '12px', color: 'var(--z-text-muted)' }}>
              {data.acuerdosVigentes.length} acuerdo{data.acuerdosVigentes.length !== 1 ? 's' : ''}
            </span>
          )}
        </div>

        <div style={{
          background: 'var(--z-bg-card)',
          border: '0.5px solid var(--z-border)',
          borderRadius: '8px',
          overflow: 'hidden',
          boxShadow: '0 1px 3px rgba(0,0,0,0.06), 0 1px 2px rgba(0,0,0,0.04)',
        }}>
          {isLoading ? (
            <CargandoSpiral mensaje="Cargando acuerdos..." />
          ) : !data || data.acuerdosVigentes.length === 0 ? (
            <div style={{ padding: '40px', textAlign: 'center', fontSize: '13px', color: 'var(--z-text-muted)' }}>
              No tienes acuerdos de pago vigentes.
            </div>
          ) : (
            <TablaAcuerdos acuerdos={data.acuerdosVigentes} />
          )}
        </div>
      </section>

      {/* ── Rendimiento ───────────────────────────────────────────────────── */}
      <section>
        <div style={{ display: 'flex', alignItems: 'flex-start', justifyContent: 'space-between', gap: '12px', marginBottom: '12px', flexWrap: 'wrap' }}>
          <div>
            <h2 style={{ fontSize: '13px', fontWeight: 600, color: 'var(--z-text-primary)', marginBottom: '2px' }}>
              Rendimiento
            </h2>
            <p style={{ fontSize: '12px', color: 'var(--z-text-muted)' }}>
              Actualizado última vez:{' '}
              <span style={{ fontWeight: 500, color: 'var(--z-text-primary)' }}>
                {isLoading ? '...' : formatearFechaActualizacion(data?.rendimiento.fechaActualizacion ?? null)}
              </span>
            </p>
          </div>

          {/* Selector de período */}
          <div style={{ display: 'flex', gap: '6px' }}>
            {OPCIONES_MES.map(op => (
              <button
                key={op.value}
                onClick={() => setMesOffset(op.value)}
                style={{
                  padding: '5px 12px',
                  borderRadius: '6px',
                  border: '0.5px solid var(--z-border)',
                  background: mesOffset === op.value ? 'var(--z-brand)' : 'var(--z-bg-page)',
                  color: mesOffset === op.value ? '#fff' : 'var(--z-text-muted)',
                  fontSize: '12px',
                  fontWeight: mesOffset === op.value ? 600 : 400,
                  cursor: 'pointer',
                  transition: 'background 0.15s, color 0.15s',
                }}
              >
                {op.label}
              </button>
            ))}
          </div>
        </div>

        {!isLoading && data && (
          <div style={{ display: 'flex', gap: '12px', flexWrap: 'wrap' }}>
            <TarjetaMetrica
              label="Contactabilidad"
              valor={formatearPorcentaje(data.rendimiento.tasaContactabilidad)}
              sub="Contactos positivos / Total gestiones"
            />
            <TarjetaMetrica
              label="Efectividad"
              valor={formatearPorcentaje(data.rendimiento.tasaEfectividad)}
              sub="Acuerdos y abonos / Contactos positivos"
            />
            <TarjetaMetrica
              label="Productividad"
              valor={String(data.rendimiento.totalGestiones)}
              sub={`Gestiones de ${rango.label}`}
            />
            {/* Proyección del ciclo: suma de ValorPromesa con FechaPromesa en el período */}
            <div style={{
              background: 'var(--z-bg-card)',
              border: '1px solid var(--z-brand)',
              borderRadius: '8px',
              padding: '20px',
              flex: '1 1 160px',
              minWidth: '160px',
              boxShadow: '0 1px 3px rgba(0,0,0,0.06), 0 1px 2px rgba(0,0,0,0.04)',
            }}>
              <p style={{ fontSize: '12px', color: 'var(--z-brand)', fontWeight: 600, marginBottom: '8px', opacity: 0.8 }}>
                Proyección del ciclo
              </p>
              <p style={{ fontSize: '18px', fontWeight: 600, color: 'var(--z-text-primary)', lineHeight: 1, wordBreak: 'break-word' }}>
                {formatearMoneda(data.rendimiento.proyeccionCiclo)}
              </p>
              <p style={{ fontSize: '11px', color: 'var(--z-text-muted)', marginTop: '6px' }}>
                Promesas con vencimiento en {rango.label}
              </p>
            </div>
          </div>
        )}

        {!isLoading && (!data || data.rendimiento.fechaActualizacion === null) && (
          <div style={{
            background: 'var(--z-bg-card)',
            border: '0.5px solid var(--z-border)',
            borderRadius: '8px',
            padding: '32px',
            textAlign: 'center',
            fontSize: '13px',
            color: 'var(--z-text-muted)',
            boxShadow: '0 1px 3px rgba(0,0,0,0.06), 0 1px 2px rgba(0,0,0,0.04)',
          }}>
            Sin datos de rendimiento disponibles.
          </div>
        )}
      </section>

      {/* ── Pagos confirmados ─────────────────────────────────────────────── */}
      <SeccionPagosConfirmados pagos={data?.pagosConfirmados} isLoading={isLoading} />

    </div>
    </>
  );
}
