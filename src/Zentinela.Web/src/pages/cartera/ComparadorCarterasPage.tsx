import { useState } from 'react';
import { useQuery } from '@tanstack/react-query';
import {
  compararCartera,
  descargarExcelComparativo,
  descargarExcelCompleto,
  obtenerOpcionesComparador,
} from '../../api/comparadorApi';
import type { ComparadorVsCarteraDto, TitularComparativoDto } from '../../types/comparador';
import { CargandoSpiral } from '../../components/CargandoSpiral';
import { ZDropdown } from '../../components/ZDropdown';

// ── Selección de cartera (misma estructura que Creador Zentinela) ─────────────

interface SeleccionCartera {
  carteraID: number;
  liderID:   number | null;
}

const claveSeleccion = (carteraID: number, liderID: number | null) =>
  `${carteraID}|${liderID ?? ''}`;

// ── Helpers ───────────────────────────────────────────────────────────────────

function fmt(n: number): string {
  return n.toLocaleString('es-CO');
}

function fmtCOP(n: number): string {
  return new Intl.NumberFormat('es-CO', {
    style: 'currency', currency: 'COP', maximumFractionDigits: 0,
  }).format(n);
}

function fmtFecha(s: string | null): string {
  if (!s) return '—';
  const d = new Date(s);
  return isNaN(d.getTime()) ? s : d.toLocaleDateString('es-CO');
}

// ── Componentes ───────────────────────────────────────────────────────────────

// Colores por categoría estandarizada (ADR-029). La clasificación vive SOLO en el
// backend (PerfilClasificacion.cs); aquí únicamente se mapea etiqueta → color.
const COLOR_CATEGORIA: Record<string, string> = {
  'Ya pago':          '#22C55E',
  'Promesa':          '#3B82F6',
  'Contacto directo': '#8B5CF6',
  'Tercero':          '#F59E0B',
  'Sin contacto':     '#F97316',
  'Sin gestión':      '#EF4444',
  'Otros':            '#6B7280',
};

function ChipCategoria({ categoria, perfil }: { categoria: string | null; perfil: string | null }) {
  if (!categoria) return <span style={{ color: 'var(--z-text-muted)', fontSize: 12 }}>Sin dato</span>;

  const color = COLOR_CATEGORIA[categoria] ?? '#6B7280';

  return (
    <span
      title={perfil ?? undefined}
      style={{
        fontSize: 11, fontWeight: 600, padding: '2px 8px',
        borderRadius: 12, background: `${color}22`, color, whiteSpace: 'nowrap',
      }}
    >
      {categoria}
    </span>
  );
}

function TablaTitulares({
  titulares,
  nombreSeleccionada,
  nombreOtra,
}: {
  titulares: TitularComparativoDto[];
  nombreSeleccionada: string;
  nombreOtra: string;
}) {
  const cols = [
    'Número Doc.', 'Nombre',
    `Categoría en ${nombreSeleccionada}`,
    `Categoría en ${nombreOtra}`,
    `Tel. ${nombreSeleccionada}`,
    `Tel. ${nombreOtra}`,
    'Saldo', 'Última gestión', 'Fecha promesa', 'Valor promesa',
  ];

  return (
    <div style={{ overflowX: 'auto', marginTop: 12 }}>
      <table style={{ width: '100%', borderCollapse: 'collapse', fontSize: 12 }}>
        <thead>
          <tr style={{ borderBottom: '2px solid var(--z-border)' }}>
            {cols.map(c => (
              <th key={c} style={{
                padding: '6px 10px', textAlign: 'left', fontWeight: 700,
                fontSize: 11, textTransform: 'uppercase', letterSpacing: '0.05em',
                color: 'var(--z-text-muted)', whiteSpace: 'nowrap',
              }}>
                {c}
              </th>
            ))}
          </tr>
        </thead>
        <tbody>
          {titulares.map(t => (
            <tr key={t.numeroDocumento} style={{ borderBottom: '1px solid var(--z-border)' }}>
              <td style={{ padding: '8px 10px', fontWeight: 600, whiteSpace: 'nowrap' }}>
                {t.numeroDocumento}
              </td>
              <td style={{ padding: '8px 10px', whiteSpace: 'nowrap' }}>{t.nombre}</td>
              <td style={{ padding: '8px 10px' }}>
                <ChipCategoria categoria={t.categoriaSeleccionada} perfil={t.perfilSeleccionada} />
              </td>
              <td style={{ padding: '8px 10px' }}>
                <ChipCategoria categoria={t.categoriaOtraCartera} perfil={t.perfilOtraCartera} />
              </td>
              <td style={{ padding: '8px 10px', whiteSpace: 'nowrap' }}>
                {t.numeroMarcadoSeleccionada ?? 'No registra'}
              </td>
              <td style={{ padding: '8px 10px', whiteSpace: 'nowrap' }}>
                {t.numeroMarcadoOtra ?? 'No registra'}
              </td>
              <td style={{ padding: '8px 10px', whiteSpace: 'nowrap', textAlign: 'right' }}>
                {t.saldoTotal != null ? fmtCOP(t.saldoTotal) : '—'}
              </td>
              <td style={{ padding: '8px 10px', whiteSpace: 'nowrap' }}>
                {fmtFecha(t.fechaUltimaGestion)}
              </td>
              <td style={{ padding: '8px 10px', whiteSpace: 'nowrap' }}>
                {fmtFecha(t.fechaPromesa)}
              </td>
              <td style={{ padding: '8px 10px', whiteSpace: 'nowrap', textAlign: 'right' }}>
                {t.valorPromesa != null ? fmtCOP(t.valorPromesa) : '—'}
              </td>
            </tr>
          ))}
        </tbody>
      </table>
    </div>
  );
}

function SeccionCartera({
  seccion,
  carteraSeleccionadaID,
  liderSeleccionadoID,
  nombreSeleccionada,
}: {
  seccion: ComparadorVsCarteraDto;
  carteraSeleccionadaID: number;
  liderSeleccionadoID: number | null;
  nombreSeleccionada: string;
}) {
  const [descargando, setDescargando] = useState(false);

  const handleDescargar = async () => {
    setDescargando(true);
    try {
      await descargarExcelComparativo(
        carteraSeleccionadaID, seccion.carteraID,
        nombreSeleccionada, seccion.nombreCartera,
        liderSeleccionadoID,
      );
    } finally {
      setDescargando(false);
    }
  };

  return (
    <div style={{
      background: 'var(--z-bg-card)',
      border: '1px solid var(--z-border)',
      borderRadius: 10,
      padding: '20px 24px',
      marginBottom: 16,
    }}>
      {/* Cabecera de sección */}
      <div style={{
        display: 'flex', alignItems: 'center',
        justifyContent: 'space-between', flexWrap: 'wrap', gap: 12,
      }}>
        <div>
          <p style={{
            fontSize: 11, fontWeight: 700, textTransform: 'uppercase',
            letterSpacing: '0.08em', color: 'var(--z-text-muted)', marginBottom: 4,
          }}>
            vs
          </p>
          <h3 style={{ fontSize: 16, fontWeight: 700, margin: 0 }}>
            {seccion.nombreCartera}
          </h3>
          <p style={{ fontSize: 13, color: 'var(--z-text-muted)', marginTop: 4 }}>
            <strong style={{ color: 'var(--z-accent)' }}>{fmt(seccion.totalCoincidencias)}</strong>
            {' '}titular{seccion.totalCoincidencias !== 1 ? 'es' : ''} con mejor categoría de gestión
            en esta cartera que en{' '}
            <strong>{nombreSeleccionada}</strong>
          </p>
        </div>

        <button
          onClick={handleDescargar}
          disabled={descargando}
          style={{
            padding: '8px 16px',
            background: descargando ? 'var(--z-border)' : 'var(--z-accent)',
            color: '#fff',
            border: 'none',
            borderRadius: 6,
            cursor: descargando ? 'not-allowed' : 'pointer',
            fontSize: 13,
            fontWeight: 600,
            whiteSpace: 'nowrap',
          }}
        >
          {descargando ? 'Generando...' : `Descargar Excel (${fmt(seccion.totalCoincidencias)})`}
        </button>
      </div>

      {/* Vista previa */}
      {seccion.preview.length > 0 && (
        <>
          <div style={{
            marginTop: 16,
            display: 'flex', alignItems: 'center', gap: 8,
          }}>
            <span style={{ fontSize: 12, color: 'var(--z-text-muted)', fontWeight: 600 }}>
              VISTA PREVIA — {seccion.preview.length} de {fmt(seccion.totalCoincidencias)}
            </span>
            {seccion.totalCoincidencias > 5 && (
              <span style={{
                fontSize: 11, padding: '1px 6px', borderRadius: 4,
                background: 'color-mix(in srgb, var(--z-accent) 15%, transparent)',
                color: 'var(--z-accent)',
              }}>
                +{fmt(seccion.totalCoincidencias - 5)} en el Excel
              </span>
            )}
          </div>
          <TablaTitulares
            titulares={seccion.preview}
            nombreSeleccionada={nombreSeleccionada}
            nombreOtra={seccion.nombreCartera}
          />
        </>
      )}
    </div>
  );
}

// ── Página principal ──────────────────────────────────────────────────────────

export function ComparadorCarterasPage() {
  const [seleccion, setSeleccion] = useState<SeleccionCartera | null>(null);
  const [descargandoCompleto, setDescargandoCompleto] = useState(false);

  const handleDescargarCompleto = async () => {
    if (!seleccion) return;
    setDescargandoCompleto(true);
    try {
      await descargarExcelCompleto(seleccion.carteraID, data!.nombreCarteraSeleccionada, seleccion.liderID);
    } finally {
      setDescargandoCompleto(false);
    }
  };

  const { data: opciones = [], isLoading: cargandoOpciones } = useQuery({
    queryKey: ['comparador-opciones'],
    queryFn: obtenerOpcionesComparador,
  });

  const { data, isLoading: cargandoDatos, isError } = useQuery({
    queryKey: ['comparador', seleccion?.carteraID, seleccion?.liderID],
    queryFn: () => compararCartera(seleccion!.carteraID, seleccion!.liderID),
    enabled: seleccion !== null,
  });

  return (
    <div style={{ maxWidth: 1300 }}>

      {/* Encabezado */}
      <div style={{ display: 'flex', justifyContent: 'space-between', alignItems: 'flex-start', flexWrap: 'wrap', gap: 12, marginBottom: 24 }}>
        <div>
          <h1 style={{ fontSize: 20, fontWeight: 600, color: 'var(--z-text-primary)', margin: 0 }}>
            Comparador de Carteras
          </h1>
          <p style={{ fontSize: 13, color: 'var(--z-text-muted)', margin: '4px 0 0' }}>
            Clasifica los titulares en categorías estandarizadas de gestión e identifica
            los que ya tienen una mejor categoría en otra cartera.
          </p>
        </div>
      </div>

      {/* Selector */}
      <div style={{ marginBottom: 28 }}>
        <label style={{ fontSize: 13, fontWeight: 600, display: 'block', marginBottom: 6 }}>
          Cartera a analizar
        </label>
        <ZDropdown
          options={[
            { value: '', label: '— Selecciona una cartera —' },
            ...opciones.map(o => ({
              value: claveSeleccion(o.carteraID, o.liderID),
              label: o.etiqueta,
            })),
          ]}
          value={seleccion ? claveSeleccion(seleccion.carteraID, seleccion.liderID) : ''}
          onChange={(v) => {
            if (!v) { setSeleccion(null); return; }
            const [cid, lid] = v.split('|');
            setSeleccion({
              carteraID: Number(cid),
              liderID:   lid === '' ? null : Number(lid),
            });
          }}
          disabled={cargandoOpciones}
          style={{ minWidth: 300 }}
        />
      </div>

      {cargandoDatos && (
        <div style={{ display: 'flex', justifyContent: 'center', marginTop: 48 }}>
          <CargandoSpiral />
        </div>
      )}

      {isError && (
        <div style={{ color: '#EF4444', fontSize: 14 }}>
          No se pudo cargar la información. Intenta de nuevo.
        </div>
      )}

      {data && (
        <>
          {/* Resumen */}
          <div style={{
            background: 'var(--z-bg-card)',
            border: '1px solid var(--z-border)',
            borderRadius: 10,
            padding: '16px 24px',
            marginBottom: 24,
            display: 'flex', gap: 32, flexWrap: 'wrap', alignItems: 'center',
          }}>
            <div>
              <p style={{ fontSize: 11, fontWeight: 700, textTransform: 'uppercase',
                          letterSpacing: '0.08em', color: 'var(--z-text-muted)', marginBottom: 4 }}>
                Cartera seleccionada
              </p>
              <p style={{ fontSize: 18, fontWeight: 700 }}>{data.nombreCarteraSeleccionada}</p>
            </div>
            <div>
              <p style={{ fontSize: 11, fontWeight: 700, textTransform: 'uppercase',
                          letterSpacing: '0.08em', color: 'var(--z-text-muted)', marginBottom: 4 }}>
                Base del comparativo
              </p>
              <p style={{ fontSize: 28, fontWeight: 700, color: '#EF4444' }}>
                {fmt(data.totalBaseComparativo)}
              </p>
            </div>
            <div>
              <p style={{ fontSize: 11, fontWeight: 700, textTransform: 'uppercase',
                          letterSpacing: '0.08em', color: 'var(--z-text-muted)', marginBottom: 4 }}>
                Carteras con oportunidades
              </p>
              <p style={{ fontSize: 28, fontWeight: 700, color: 'var(--z-accent)' }}>
                {data.comparaciones.length}
              </p>
            </div>
            {data.desglosePorCategoria.length > 0 && (
              <div>
                <p style={{ fontSize: 11, fontWeight: 700, textTransform: 'uppercase',
                            letterSpacing: '0.08em', color: 'var(--z-text-muted)', marginBottom: 6 }}>
                  Cartera por categoría
                </p>
                <div style={{ display: 'flex', gap: 6, flexWrap: 'wrap', maxWidth: 420 }}>
                  {data.desglosePorCategoria.map(c => (
                    <span key={c.categoria} style={{
                      fontSize: 11, fontWeight: 600, padding: '2px 8px', borderRadius: 12,
                      background: `${COLOR_CATEGORIA[c.categoria] ?? '#6B7280'}22`,
                      color: COLOR_CATEGORIA[c.categoria] ?? '#6B7280',
                      whiteSpace: 'nowrap',
                    }}>
                      {c.categoria}: {fmt(c.total)}
                    </span>
                  ))}
                </div>
              </div>
            )}

            {data.comparaciones.length > 0 && (
              <div style={{ marginLeft: 'auto' }}>
                <button
                  onClick={handleDescargarCompleto}
                  disabled={descargandoCompleto}
                  style={{
                    padding: '10px 20px',
                    background: descargandoCompleto ? 'var(--z-border)' : '#16A34A',
                    color: '#fff',
                    border: 'none',
                    borderRadius: 6,
                    cursor: descargandoCompleto ? 'not-allowed' : 'pointer',
                    fontSize: 13,
                    fontWeight: 600,
                    whiteSpace: 'nowrap',
                  }}
                >
                  {descargandoCompleto ? 'Generando...' : 'Descargar Excel completo'}
                </button>
                <p style={{ fontSize: 11, color: 'var(--z-text-muted)', marginTop: 4, textAlign: 'center' }}>
                  Todas las carteras combinadas
                </p>
              </div>
            )}
          </div>

          {data.comparaciones.length === 0 ? (
            <div style={{
              background: 'var(--z-bg-card)', border: '1px solid var(--z-border)',
              borderRadius: 10, padding: '32px 24px', textAlign: 'center',
              color: 'var(--z-text-muted)', fontSize: 14,
            }}>
              No se encontraron titulares con mejor estado en otras carteras.
            </div>
          ) : (
            data.comparaciones.map(seccion => (
              <SeccionCartera
                key={seccion.carteraID}
                seccion={seccion}
                carteraSeleccionadaID={data.carteraID}
                liderSeleccionadoID={seleccion?.liderID ?? null}
                nombreSeleccionada={data.nombreCarteraSeleccionada}
              />
            ))
          )}
        </>
      )}
    </div>
  );
}
