// Presentación del análisis IVR, con la estructura del informe "ANALISIS ROBOCALL":
// resumen global → apreciación → embudo → comparativo por líder → detalle por líder
// (embudo, desenlaces y carteras).
//
// No incluye "Solicitadas"/"Enviadas" (no existen en el modelo de carga única) ni
// "Atendidos"/"% Atención", que salen del CDR y aún no se cargan en Zentinela.
import { useState } from 'react';
import type { AnalisisIvr, AnalisisIvrLider, DesenlaceIvr, EmbudoPasoIvr } from '../../../types/virtualidad';

const n = (v: number) => v.toLocaleString('es-CO');
const pct = (v: number) => `${v.toFixed(2)}%`;

const COLORES = {
  ivr:    '#0891b2',
  verde:  '#16a34a',
  ambar:  '#d97706',
  rojo:   '#dc2626',
  gris:   '#64748b',
};

// Escala de la barra del embudo: 28 bloques, como el documento original.
const BLOQUES = 28;

function BarraBloques({ porcentaje, color }: { porcentaje: number; color: string }) {
  const llenos = Math.max(1, Math.round((porcentaje / 100) * BLOQUES));
  return (
    <span style={{ fontFamily: 'monospace', letterSpacing: '-1px', fontSize: 13, whiteSpace: 'nowrap' }}>
      <span style={{ color }}>{'█'.repeat(Math.min(llenos, BLOQUES))}</span>
      <span style={{ color: 'var(--z-border)' }}>{'░'.repeat(Math.max(0, BLOQUES - llenos))}</span>
    </span>
  );
}

function Kpi({ label, valor, sufijo, color }: {
  label: string; valor: string; sufijo?: string; color?: string;
}) {
  return (
    <div style={{
      background: 'var(--z-bg-card)', border: '1px solid var(--z-border)', borderRadius: 10,
      padding: '14px 18px', flex: 1, minWidth: 150,
      boxShadow: '0 2px 8px rgba(0,0,0,0.05)',
    }}>
      <p style={{
        fontSize: 10, color: 'var(--z-text-muted)', margin: '0 0 4px',
        textTransform: 'uppercase', letterSpacing: '0.06em', fontWeight: 600,
      }}>
        {label}
      </p>
      <p style={{ fontSize: 22, fontWeight: 700, margin: 0, color: color ?? 'var(--z-text-primary)' }}>
        {valor}
        {sufijo && <span style={{ fontSize: 13, fontWeight: 400, color: 'var(--z-text-muted)', marginLeft: 4 }}>{sufijo}</span>}
      </p>
    </div>
  );
}

function Embudo({ pasos }: { pasos: EmbudoPasoIvr[] }) {
  return (
    <table style={{ width: '100%', borderCollapse: 'collapse', fontSize: 13 }}>
      <tbody>
        {pasos.map((p, i) => (
          <tr key={p.etiqueta}>
            <td style={{ padding: '6px 10px 6px 0', whiteSpace: 'nowrap', color: 'var(--z-text-primary)' }}>
              {p.etiqueta}
            </td>
            <td style={{ padding: '6px 10px', width: 1 }}>
              <BarraBloques porcentaje={i === 0 ? 100 : p.porcentaje} color={COLORES.ivr} />
            </td>
            <td style={{ padding: '6px 10px', textAlign: 'right', fontVariantNumeric: 'tabular-nums', fontWeight: 600 }}>
              {n(p.clientes)}
            </td>
            <td style={{ padding: '6px 0 6px 10px', textAlign: 'right', color: 'var(--z-text-muted)', fontVariantNumeric: 'tabular-nums', whiteSpace: 'nowrap' }}>
              {pct(p.porcentaje)}{p.base !== 'base' && p.base !== 'de marcados' ? ` ${p.base}` : ''}
            </td>
          </tr>
        ))}
      </tbody>
    </table>
  );
}

function TablaDesenlaces({ desenlaces }: { desenlaces: DesenlaceIvr[] }) {
  const max = Math.max(1, ...desenlaces.map(d => d.porcentaje));
  return (
    <table style={{ width: '100%', borderCollapse: 'collapse', fontSize: 13 }}>
      <thead>
        <tr>
          {['Desenlace', 'Clientes', '% contact.', ''].map((h, i) => (
            <th key={h || i} style={{
              padding: '8px 10px', fontSize: 10, fontWeight: 600, letterSpacing: '0.05em',
              textTransform: 'uppercase', color: 'var(--z-text-muted)',
              textAlign: i === 0 || i === 3 ? 'left' : 'right',
              borderBottom: '1px solid var(--z-border)',
            }}>
              {h}
            </th>
          ))}
        </tr>
      </thead>
      <tbody>
        {desenlaces.map(d => (
          <tr key={d.rango}>
            <td style={{ padding: '7px 10px', borderBottom: '0.5px solid var(--z-border)' }}>{d.desenlace}</td>
            <td style={{ padding: '7px 10px', textAlign: 'right', fontVariantNumeric: 'tabular-nums', fontWeight: 600, borderBottom: '0.5px solid var(--z-border)' }}>
              {n(d.clientes)}
            </td>
            <td style={{ padding: '7px 10px', textAlign: 'right', fontVariantNumeric: 'tabular-nums', color: 'var(--z-text-muted)', borderBottom: '0.5px solid var(--z-border)' }}>
              {pct(d.porcentaje)}
            </td>
            <td style={{ padding: '7px 10px', borderBottom: '0.5px solid var(--z-border)' }}>
              <span style={{ fontFamily: 'monospace', color: COLORES.ivr, letterSpacing: '-1px' }}>
                {'█'.repeat(Math.max(1, Math.round((d.porcentaje / max) * 10)))}
              </span>
            </td>
          </tr>
        ))}
      </tbody>
    </table>
  );
}

function CajaApreciacion({ texto, alerta }: { texto: string; alerta?: boolean }) {
  if (!texto) return null;
  return (
    <div style={{
      background: alerta ? '#fffbeb' : 'var(--z-bg-page)',
      border: `1px solid ${alerta ? '#fbbf24' : 'var(--z-border)'}`,
      borderRadius: 8, padding: '12px 16px',
      fontSize: 13, lineHeight: 1.55, color: alerta ? '#92400e' : 'var(--z-text-primary)',
    }}>
      {alerta && <strong style={{ display: 'block', marginBottom: 4 }}>ATENCIÓN: CONTACTABILIDAD BAJA</strong>}
      {texto}
    </div>
  );
}

function FilaLider({ lider, expandido, onToggle }: {
  lider: AnalisisIvrLider; expandido: boolean; onToggle: () => void;
}) {
  return (
    <>
      <tr
        onClick={onToggle}
        style={{ cursor: 'pointer' }}
        onMouseEnter={e => { e.currentTarget.style.background = 'var(--z-bg-page)'; }}
        onMouseLeave={e => { e.currentTarget.style.background = 'transparent'; }}
      >
        <td style={{ ...tdBase, fontWeight: 600 }}>
          <span style={{ color: 'var(--z-text-muted)', fontSize: 10, marginRight: 6 }}>{expandido ? '▼' : '▶'}</span>
          {lider.nombreLider}
        </td>
        <td style={tdNum}>{n(lider.clientesConResultado)}</td>
        <td style={tdNum}>{n(lider.contactados)}</td>
        <td style={{ ...tdNum, color: COLORES.ivr, fontWeight: 600 }}>{pct(lider.contactabilidad)}</td>
        <td style={tdNum}>{n(lider.escucharonMenu)}</td>
        <td style={tdNum}>{n(lider.marcaronOpcion1)}</td>
        <td style={{ ...tdNum, color: COLORES.verde, fontWeight: 600 }}>{pct(lider.conversionOpcion1)}</td>
      </tr>
      {expandido && (
        <tr>
          <td colSpan={7} style={{ padding: '0 0 18px', background: 'var(--z-bg-page)' }}>
            <div style={{ display: 'flex', flexDirection: 'column', gap: 14, padding: '14px 18px' }}>
              <p style={{ margin: 0, fontSize: 12, color: 'var(--z-text-muted)' }}>
                {lider.totalCarteras} cartera(s) en el período
              </p>

              <Embudo pasos={lider.embudo} />

              <div>
                <p style={{ margin: '0 0 6px', fontSize: 12, fontWeight: 600 }}>Desenlace de los contactados</p>
                <TablaDesenlaces desenlaces={lider.desenlaces} />
              </div>

              <div>
                <p style={{ margin: '0 0 6px', fontSize: 12, fontWeight: 600 }}>Detalle por cartera</p>
                <table style={{ width: '100%', borderCollapse: 'collapse', fontSize: 12 }}>
                  <thead>
                    <tr>
                      {['Cartera', 'Con result.', 'Contact.', '% Cont.', 'Marc. 1'].map((h, i) => (
                        <th key={h} style={{ ...thBase, textAlign: i === 0 ? 'left' : 'right' }}>{h}</th>
                      ))}
                    </tr>
                  </thead>
                  <tbody>
                    {lider.porCartera.map(c => (
                      <tr key={c.carteraID}>
                        <td style={tdBase}>{c.nombreCartera}</td>
                        <td style={tdNum}>{n(c.clientesConResultado)}</td>
                        <td style={tdNum}>{n(c.contactados)}</td>
                        <td style={{ ...tdNum, color: COLORES.ivr }}>{pct(c.contactabilidad)}</td>
                        <td style={tdNum}>{n(c.marcaronOpcion1)}</td>
                      </tr>
                    ))}
                  </tbody>
                </table>
              </div>

              <CajaApreciacion texto={lider.apreciacion} />
            </div>
          </td>
        </tr>
      )}
    </>
  );
}

const thBase: React.CSSProperties = {
  padding: '8px 10px', fontSize: 10, fontWeight: 600, letterSpacing: '0.05em',
  textTransform: 'uppercase', color: 'var(--z-text-muted)', whiteSpace: 'nowrap',
  borderBottom: '1px solid var(--z-border)',
};
const tdBase: React.CSSProperties = {
  padding: '8px 10px', borderBottom: '0.5px solid var(--z-border)',
  color: 'var(--z-text-primary)', whiteSpace: 'nowrap',
};
const tdNum: React.CSSProperties = {
  ...tdBase, textAlign: 'right', fontVariantNumeric: 'tabular-nums',
};

export function AnalisisIvrPanel({ analisis }: { analisis: AnalisisIvr }) {
  const [expandido, setExpandido] = useState<number | null>(null);

  if (analisis.clientesMarcados === 0 || analisis.clientesConResultado === 0) {
    return (
      <div style={{
        background: 'var(--z-bg-page)', border: '1px solid var(--z-border)', borderRadius: 8,
        padding: '20px 24px', fontSize: 13, color: 'var(--z-text-muted)', textAlign: 'center',
      }}>
        {analisis.clientesMarcados === 0
          ? 'No hay registros de IVR en el período seleccionado.'
          : `Los ${n(analisis.clientesMarcados)} clientes marcados en el período no tienen resultado cargado, así que todavía no se puede medir contactabilidad.`}
        <div style={{ fontSize: 12, marginTop: 6 }}>
          Los resultados se cargan en <strong>Administración → Gestión de Histórico de Virtualidad</strong>.
        </div>
      </div>
    );
  }

  return (
    <div style={{ display: 'flex', flexDirection: 'column', gap: 18 }}>

      {/* Los clientes sin resultado NO son "no contactados": es información que falta.
          Se avisa arriba para que nadie lea el porcentaje como si fuera desempeño. */}
      {analisis.sinResultado > 0 && (
        <div style={{
          background: '#fffbeb', border: '1px solid #fbbf24', borderRadius: 8,
          padding: '12px 16px', fontSize: 13, color: '#92400e', lineHeight: 1.5,
        }}>
          <strong>{n(analisis.sinResultado)} de {n(analisis.clientesMarcados)} clientes no tienen
          resultado cargado</strong> y quedan fuera del cálculo. Vienen del flujo anterior de dos
          fases, que registraba el envío pero nunca recibió su resultado. Los porcentajes de abajo se
          calculan sobre los {n(analisis.clientesConResultado)} que sí lo tienen.
        </div>
      )}

      {/* Resumen global */}
      <div style={{ display: 'flex', gap: 12, flexWrap: 'wrap' }}>
        <Kpi label="Clientes con resultado" valor={n(analisis.clientesConResultado)} />
        <Kpi label="Contactados"        valor={n(analisis.contactados)}      color={COLORES.ivr} />
        <Kpi label="Escuchó el menú"    valor={n(analisis.escucharonMenu)} />
        <Kpi label="Marcaron opción 1"  valor={n(analisis.marcaronOpcion1)}  color={COLORES.verde} />
      </div>

      <div style={{ display: 'flex', gap: 12, flexWrap: 'wrap' }}>
        <Kpi label="Contactabilidad"          valor={pct(analisis.contactabilidad)}
             color={analisis.contactabilidad < 30 ? COLORES.ambar : COLORES.verde} />
        <Kpi label="Conversión a opción 1"    valor={pct(analisis.conversionOpcion1)} />
        <Kpi label="Marcaciones totales"      valor={n(analisis.totalMarcaciones)}
             sufijo={`en ${analisis.totalCarteras} cartera(s)`} color={COLORES.gris} />
      </div>

      <CajaApreciacion texto={analisis.apreciacion} alerta={analisis.contactabilidad < 30} />

      {/* Embudo global */}
      <div>
        <p style={{ margin: '0 0 8px', fontSize: 13, fontWeight: 600 }}>Porcentaje general</p>
        <Embudo pasos={analisis.embudo} />
      </div>

      {/* Desenlaces globales */}
      <div>
        <p style={{ margin: '0 0 8px', fontSize: 13, fontWeight: 600 }}>Desenlace de los contactados</p>
        <TablaDesenlaces desenlaces={analisis.desenlaces} />
      </div>

      {/* Comparativo por líder */}
      <div>
        <p style={{ margin: '0 0 8px', fontSize: 13, fontWeight: 600 }}>
          Comparativo por líder
          <span style={{ fontWeight: 400, fontSize: 12, color: 'var(--z-text-muted)', marginLeft: 8 }}>
            — clic en una fila para ver su detalle
          </span>
        </p>
        <div style={{ overflowX: 'auto', border: '1px solid var(--z-border)', borderRadius: 8 }}>
          <table style={{ width: '100%', borderCollapse: 'collapse', fontSize: 13 }}>
            <thead>
              <tr>
                {['Líder', 'Con result.', 'Contact.', '% Cont.', 'Escuchó menú', 'Marc. 1', '% Conv.'].map((h, i) => (
                  <th key={h} style={{ ...thBase, textAlign: i === 0 ? 'left' : 'right' }}>{h}</th>
                ))}
              </tr>
            </thead>
            <tbody>
              {analisis.porLider.map(l => (
                <FilaLider
                  key={l.liderID}
                  lider={l}
                  expandido={expandido === l.liderID}
                  onToggle={() => setExpandido(expandido === l.liderID ? null : l.liderID)}
                />
              ))}
              <tr style={{ background: 'var(--z-brand-light)', fontWeight: 700 }}>
                <td style={tdBase}>TOTAL</td>
                <td style={tdNum}>{n(analisis.clientesConResultado)}</td>
                <td style={tdNum}>{n(analisis.contactados)}</td>
                <td style={{ ...tdNum, color: COLORES.ivr }}>{pct(analisis.contactabilidad)}</td>
                <td style={tdNum}>{n(analisis.escucharonMenu)}</td>
                <td style={tdNum}>{n(analisis.marcaronOpcion1)}</td>
                <td style={{ ...tdNum, color: COLORES.verde }}>{pct(analisis.conversionOpcion1)}</td>
              </tr>
            </tbody>
          </table>
        </div>
      </div>

      {/* Nota metodológica — equivale a la del informe original */}
      <div style={{
        borderTop: '1px solid var(--z-border)', paddingTop: 12,
        fontSize: 12, color: 'var(--z-text-muted)', lineHeight: 1.6,
      }}>
        <p style={{ margin: 0 }}>
          Todos los porcentajes se calculan sobre <strong>clientes únicos por cartera</strong>. Un
          cliente marcado varias veces cuenta una sola vez, con su mejor desenlace según la jerarquía:
          marcó 1 &gt; escuchó sin marcar &gt; sin marcación válida &gt; solo audio &gt; colgó.
        </p>
        <p style={{ margin: '6px 0 0' }}>
          <strong>Sobre "sin marcación válida":</strong> el IVR solo registra la opción cuando es
          válida y dispara la transferencia. Una tecla equivocada no queda registrada y es
          indistinguible de no marcar nada, por lo que esta categoría agrupa ambos casos.
        </p>
        <p style={{ margin: '6px 0 0' }}>
          Quedan fuera de "contactados" los desenlaces sin contacto real: congestión de red, errores
          de proceso o de sistema, y pasos internos del marcador.
        </p>
      </div>
    </div>
  );
}
