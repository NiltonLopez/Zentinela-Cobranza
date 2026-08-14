import { useMemo, useState } from 'react';
import { ZDropdown } from '../../../components/ZDropdown';
import { TD, ThSort, useSortable } from '../../../components/informes/reporteCompartido';
import type { UsoPorLider, UsoPorLiderCartera } from '../../../types/informesGenerales';

type Agrupacion = 'lider' | 'lider-cartera';

/** Fila normalizada: ambas agrupaciones se renderizan con la misma tabla. */
interface Fila extends Record<string, unknown> {
  clave: string;
  lider: string;
  cartera: string | null;
  numerosAcreditados: number;
  titularesDistintos: number;
  /** Nº de carteras (agrupación por líder) — null en la vista líder × cartera. */
  carterasDistintas: number | null;
  titularesAsignados: number;
  /** Cuota sobre el total encontrado. */
  porcentaje: number;
  /** Uso real de su propia base. null si el líder no tiene asignación. */
  porcentajeCobertura: number | null;
  esExterna: boolean;
  relacionInactiva: boolean;
  sinRelacionLiderCartera: boolean;
  sinLider: boolean;
}

/**
 * Uso de la base del líder, con barra para comparar filas de un vistazo.
 * Sin asignación no se inventa un 0 %: no hay contra qué medir, se muestra "—".
 */
function BarraCobertura({ valor }: { valor: number | null }) {
  if (valor === null) {
    return <span style={{ fontSize: 12, color: 'var(--z-text-muted)' }}>—</span>;
  }
  // Rojo < 25 % (base muy poco explotada) · ámbar < 60 % · verde ≥ 60 %.
  const color = valor >= 60 ? '#16a34a' : valor >= 25 ? '#ca8a04' : '#dc2626';
  return (
    <div style={{ display: 'flex', alignItems: 'center', justifyContent: 'flex-end', gap: 8 }}>
      <div style={{
        width: 60, height: 6, borderRadius: 3, flexShrink: 0,
        background: 'var(--z-border)', overflow: 'hidden',
      }}>
        <div style={{
          width: `${Math.min(100, valor)}%`, height: '100%',
          background: color, borderRadius: 3,
        }} />
      </div>
      <span style={{ fontVariantNumeric: 'tabular-nums', fontWeight: 600, color, minWidth: 52, textAlign: 'right' }}>
        {/* Un uso real pero minúsculo redondea a "0,00%" y se lee como cero absoluto. */}
        {valor > 0 && valor < 0.01 ? '<0,01%' : `${valor.toFixed(2)}%`}
      </span>
    </div>
  );
}

function Badge({ texto, color, fondo }: { texto: string; color: string; fondo: string }) {
  return (
    <span style={{
      fontSize: 10, fontWeight: 700, padding: '2px 6px', borderRadius: 4,
      background: fondo, color, whiteSpace: 'nowrap', marginLeft: 6,
    }}>
      {texto}
    </span>
  );
}

export function TablaPorLider({ porLider, porLiderCartera, numerosEnBaseDatos }: {
  porLider: UsoPorLider[];
  porLiderCartera: UsoPorLiderCartera[];
  numerosEnBaseDatos: number;
}) {
  const [agrupacion, setAgrupacion] = useState<Agrupacion>('lider');

  const filas = useMemo<Fila[]>(() => {
    if (agrupacion === 'lider') {
      return porLider.map((l) => ({
        clave: `l-${l.liderID ?? 'null'}`,
        lider: l.lider,
        cartera: null,
        numerosAcreditados: l.numerosAcreditados,
        titularesDistintos: l.titularesDistintos,
        carterasDistintas: l.carterasDistintas,
        titularesAsignados: l.titularesAsignados,
        porcentaje: l.porcentaje,
        porcentajeCobertura: l.porcentajeCobertura,
        esExterna: false,
        relacionInactiva: l.relacionInactiva,
        sinRelacionLiderCartera: l.sinRelacionLiderCartera,
        sinLider: l.liderID === null,
      }));
    }
    return porLiderCartera.map((lc) => ({
      clave: `lc-${lc.liderID ?? 'null'}-${lc.carteraID ?? 'null'}`,
      lider: lc.lider,
      cartera: lc.cartera,
      numerosAcreditados: lc.numerosAcreditados,
      titularesDistintos: lc.titularesDistintos,
      carterasDistintas: null,
      titularesAsignados: lc.titularesAsignados,
      porcentaje: numerosEnBaseDatos > 0
        ? Math.round((10000 * lc.numerosAcreditados) / numerosEnBaseDatos) / 100
        : 0,
      porcentajeCobertura: lc.porcentajeCobertura,
      esExterna: lc.esExterna,
      relacionInactiva: lc.relacionInactiva,
      sinRelacionLiderCartera: lc.sinRelacionLiderCartera,
      sinLider: lc.liderID === null,
    }));
  }, [agrupacion, porLider, porLiderCartera, numerosEnBaseDatos]);

  const { sorted, sortKey, sortDir, handleSort } = useSortable<Fila>(filas);

  // La fila "(sin líder asignado)" siempre va al final y nunca se filtra: es el bucket
  // residual y ocultarlo sería, exactamente, perder dinero de vista en silencio.
  const ordenadas = useMemo(
    () => [...sorted].sort((a, b) => Number(a.sinLider) - Number(b.sinLider)),
    [sorted],
  );

  const porCartera = agrupacion === 'lider-cartera';

  return (
    <div style={{ display: 'flex', flexDirection: 'column', gap: 10 }}>
      <div style={{ display: 'flex', justifyContent: 'space-between', alignItems: 'center', flexWrap: 'wrap', gap: 10 }}>
        <div>
          <p style={{ fontSize: 14, fontWeight: 600, color: 'var(--z-text-primary)', margin: 0 }}>
            Acreditación por líder
          </p>
          <p style={{ fontSize: 12, color: 'var(--z-text-muted)', margin: '3px 0 0' }}>
            <strong>Uso de su base</strong> = titulares usados ÷ titulares asignados (qué tanto se
            explotó su propia asignación). <strong>Cuota del total</strong> = su parte del total
            encontrado. Se cuenta en titulares, no en números, porque un titular puede tener varios
            teléfonos.
          </p>
        </div>
        <ZDropdown<Agrupacion>
          options={[
            { value: 'lider',         label: 'Por líder' },
            { value: 'lider-cartera', label: 'Por líder y cartera' },
          ]}
          value={agrupacion}
          onChange={setAgrupacion}
          background="var(--z-bg-page)"
        />
      </div>

      <div style={{ overflowX: 'auto', border: '1px solid var(--z-border)', borderRadius: 10 }}>
        <table style={{ width: '100%', borderCollapse: 'collapse', fontSize: 13 }}>
          <thead>
            <tr>
              <ThSort label="Líder" k="lider" sortKey={sortKey as string | null} sortDir={sortDir} onSort={handleSort as (k: string) => void} align="left" />
              {porCartera && (
                <ThSort label="Cartera" k="cartera" sortKey={sortKey as string | null} sortDir={sortDir} onSort={handleSort as (k: string) => void} align="left" />
              )}
              <ThSort label="Números acreditados" k="numerosAcreditados" sortKey={sortKey as string | null} sortDir={sortDir} onSort={handleSort as (k: string) => void} />
              <ThSort label="Titulares usados" k="titularesDistintos" sortKey={sortKey as string | null} sortDir={sortDir} onSort={handleSort as (k: string) => void} />
              <ThSort label="Titulares asignados" k="titularesAsignados" sortKey={sortKey as string | null} sortDir={sortDir} onSort={handleSort as (k: string) => void} />
              <ThSort label="Uso de su base" k="porcentajeCobertura" sortKey={sortKey as string | null} sortDir={sortDir} onSort={handleSort as (k: string) => void} />
              <ThSort label="Cuota del total" k="porcentaje" sortKey={sortKey as string | null} sortDir={sortDir} onSort={handleSort as (k: string) => void} />
              {!porCartera && (
                <ThSort label="Carteras" k="carterasDistintas" sortKey={sortKey as string | null} sortDir={sortDir} onSort={handleSort as (k: string) => void} />
              )}
            </tr>
          </thead>
          <tbody>
            {ordenadas.map((f) => (
              <tr
                key={f.clave}
                style={{ background: f.sinLider ? '#fffbeb' : 'transparent' }}
                onMouseEnter={(e) => { if (!f.sinLider) e.currentTarget.style.background = 'var(--z-bg-page)'; }}
                onMouseLeave={(e) => { if (!f.sinLider) e.currentTarget.style.background = 'transparent'; }}
              >
                <td style={{ ...TD, fontWeight: f.sinLider ? 600 : 400, color: f.sinLider ? '#b45309' : 'var(--z-text-primary)' }}>
                  {f.lider}
                  {f.relacionInactiva && <Badge texto="Inactiva" color="#475569" fondo="#e2e8f0" />}
                  {f.esExterna && <Badge texto="Externa" color="#c2410c" fondo="#ffedd5" />}
                  {f.sinRelacionLiderCartera && (
                    <Badge texto="Sin relación en Lider_Cartera" color="#b91c1c" fondo="#fee2e2" />
                  )}
                </td>
                {porCartera && <td style={TD}>{f.cartera}</td>}
                <td style={{ ...TD, textAlign: 'right', fontVariantNumeric: 'tabular-nums', fontWeight: 600 }}>
                  {f.numerosAcreditados.toLocaleString('es-CO')}
                </td>
                <td style={{ ...TD, textAlign: 'right', fontVariantNumeric: 'tabular-nums' }}>
                  {f.titularesDistintos.toLocaleString('es-CO')}
                </td>
                <td style={{ ...TD, textAlign: 'right', fontVariantNumeric: 'tabular-nums', color: 'var(--z-text-muted)' }}>
                  {f.titularesAsignados > 0 ? f.titularesAsignados.toLocaleString('es-CO') : '—'}
                </td>
                <td style={{ ...TD, textAlign: 'right' }}>
                  <BarraCobertura valor={f.porcentajeCobertura} />
                </td>
                <td style={{ ...TD, textAlign: 'right', fontVariantNumeric: 'tabular-nums', color: 'var(--z-text-muted)' }}>
                  {f.porcentaje.toFixed(2)}%
                </td>
                {!porCartera && (
                  <td style={{ ...TD, textAlign: 'right', fontVariantNumeric: 'tabular-nums' }}>
                    {f.carterasDistintas?.toLocaleString('es-CO') ?? '—'}
                  </td>
                )}
              </tr>
            ))}
            {ordenadas.length === 0 && (
              <tr>
                <td colSpan={7} style={{ ...TD, textAlign: 'center', color: 'var(--z-text-muted)', padding: '24px 12px' }}>
                  Ningún número encontrado se pudo atribuir a un líder.
                </td>
              </tr>
            )}
          </tbody>
        </table>
      </div>
    </div>
  );
}
