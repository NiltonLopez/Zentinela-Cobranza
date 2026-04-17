import { useState, useRef } from 'react';
import { useMutation, useQuery } from '@tanstack/react-query';
import {
  BarChart, Bar, XAxis, YAxis, Tooltip, ResponsiveContainer,
  PieChart, Pie, Cell, Legend,
} from 'recharts';
import {
  validarMensajeria, confirmarMensajeria, getInformeMensajeria,
  validarIVRPreEnvio, confirmarIVRPreEnvio,
  validarIVRPostEnvio, confirmarIVRPostEnvio, getInformeIVR,
  validarLlamadasPerdidas, confirmarLlamadasPerdidas, getInformeLlamadasPerdidas,
  validarLlamadasEntrantes, confirmarLlamadasEntrantes, getInformeEntrantes,
  getInformePagosImpacto,
} from '../../api/virtualidadApi';
import type {
  ValidarMensajeriaResponse, FilaMensajeria,
  ValidarIVRResponse, FilaIVR,
  ValidarLlamadasResponse, FilaLlamada,
  ValidarEntrantesResponse,
} from '../../types/virtualidad';

// ── Paleta ────────────────────────────────────────────────────────────────────
const C = {
  brand:    '#2563eb',
  success:  '#16a34a',
  warning:  '#d97706',
  danger:   '#dc2626',
  gray:     '#64748b',
  mensajes: '#7c3aed',
  ivr:      '#0891b2',
  llamadas: '#059669',
  entrantes:'#d97706',
  impacto:  '#2563eb',
};

type Tab = 'mensajeria' | 'ivr' | 'llamadas-perdidas' | 'llamadas-entrantes' | 'pagos-impacto';

const AÑO_HOY = new Date().getFullYear();

// ── Selector de período (copiado del Dashboard de Directores) ─────────────────

type Preset = 'hoy' | 'ciclo' | 'mes-pasado' | '7d' | '30d' | 'custom';

const PRESETS_PERIODO: { valor: Preset; label: string }[] = [
  { valor: 'hoy',        label: 'Hoy'             },
  { valor: 'ciclo',      label: 'Este mes'         },
  { valor: 'mes-pasado', label: 'Mes pasado'       },
  { valor: '7d',         label: 'Últimos 7 días'   },
  { valor: '30d',        label: 'Últimos 30 días'  },
  { valor: 'custom',     label: 'Personalizado'    },
];

function isoFecha(d: Date) { return d.toISOString().slice(0, 10); }
function isoHoy()          { return isoFecha(new Date()); }

const HOY    = isoHoy();
const HACE30 = (() => { const d = new Date(); d.setDate(d.getDate() - 29); return isoFecha(d); })();

function calcularRangoPeriodo(preset: Preset, customDesde: string, customHasta: string): { desde: string; hasta: string } {
  const hoy = new Date();
  switch (preset) {
    case 'hoy':        return { desde: isoHoy(), hasta: isoHoy() };
    case 'ciclo':      return { desde: isoFecha(new Date(hoy.getFullYear(), hoy.getMonth(), 1)), hasta: isoHoy() };
    case 'mes-pasado': return {
      desde: isoFecha(new Date(hoy.getFullYear(), hoy.getMonth() - 1, 1)),
      hasta: isoFecha(new Date(hoy.getFullYear(), hoy.getMonth(), 0)),
    };
    case '7d':  { const d = new Date(hoy); d.setDate(hoy.getDate() - 6);  return { desde: isoFecha(d), hasta: isoHoy() }; }
    case '30d': { const d = new Date(hoy); d.setDate(hoy.getDate() - 29); return { desde: isoFecha(d), hasta: isoHoy() }; }
    case 'custom': return { desde: customDesde || isoHoy(), hasta: customHasta || isoHoy() };
  }
}

function SelectorPeriodo({ onChange }: { onChange: (desde: string, hasta: string) => void }) {
  const [preset,      setPreset]      = useState<Preset>('30d');
  const [customDesde, setCustomDesde] = useState('');
  const [customHasta, setCustomHasta] = useState('');

  const estilo = (activo: boolean): React.CSSProperties => ({
    padding: '5px 12px', borderRadius: '20px',
    border: '0.5px solid var(--z-border)',
    background: activo ? 'var(--z-text-primary)' : 'var(--z-bg-page)',
    color:      activo ? 'var(--z-bg-page)'       : 'var(--z-text-muted)',
    fontSize: '12px', fontWeight: activo ? 600 : 400, cursor: 'pointer',
  });

  const aplicar = (p: Preset, cd = customDesde, ch = customHasta) => {
    const { desde, hasta } = calcularRangoPeriodo(p, cd, ch);
    onChange(desde, hasta);
  };

  return (
    <div>
      <p style={{ fontSize: '11px', fontWeight: 600, color: 'var(--z-text-muted)', textTransform: 'uppercase', letterSpacing: '0.04em', marginBottom: '8px' }}>
        Período
      </p>
      <div style={{ display: 'flex', gap: '6px', flexWrap: 'wrap', alignItems: 'center' }}>
        {PRESETS_PERIODO.map(p => (
          <button key={p.valor} style={estilo(preset === p.valor)}
            onClick={() => { setPreset(p.valor); if (p.valor !== 'custom') aplicar(p.valor); }}>
            {p.label}
          </button>
        ))}
      </div>
      {preset === 'custom' && (
        <div style={{ display: 'flex', gap: '10px', marginTop: '10px', flexWrap: 'wrap', alignItems: 'flex-end' }}>
          <div style={{ display: 'flex', flexDirection: 'column', gap: '4px' }}>
            <label style={{ fontSize: '11px', color: 'var(--z-text-muted)' }}>Desde</label>
            <input type="date" value={customDesde} onChange={e => setCustomDesde(e.target.value)}
              style={{ padding: '6px 10px', fontSize: '13px', borderRadius: '6px', border: '0.5px solid var(--z-border)', background: 'var(--z-bg-page)', color: 'var(--z-text-primary)', outline: 'none' }} />
          </div>
          <div style={{ display: 'flex', flexDirection: 'column', gap: '4px' }}>
            <label style={{ fontSize: '11px', color: 'var(--z-text-muted)' }}>Hasta</label>
            <input type="date" value={customHasta} onChange={e => setCustomHasta(e.target.value)}
              style={{ padding: '6px 10px', fontSize: '13px', borderRadius: '6px', border: '0.5px solid var(--z-border)', background: 'var(--z-bg-page)', color: 'var(--z-text-primary)', outline: 'none' }} />
          </div>
          <button onClick={() => aplicar('custom')}
            style={{ padding: '6px 14px', borderRadius: '6px', border: 'none', background: C.brand, color: '#fff', fontSize: '13px', cursor: 'pointer' }}>
            Aplicar
          </button>
        </div>
      )}
    </div>
  );
}

// ── Componentes compartidos ───────────────────────────────────────────────────

function KpiCard({ label, value, color = C.brand }: { label: string; value: string | number; color?: string }) {
  return (
    <div style={{ background: 'var(--z-bg-card)', border: '1px solid var(--z-border)', borderRadius: 8, padding: '12px 20px', minWidth: 120 }}>
      <div style={{ fontSize: 22, fontWeight: 700, color }}>{value}</div>
      <div style={{ fontSize: 12, color: C.gray }}>{label}</div>
    </div>
  );
}

function DropZone({ onFile, accept = '.xlsx', label }: { onFile: (f: File) => void; accept?: string; label?: string }) {
  const ref = useRef<HTMLInputElement>(null);
  return (
    <div
      onClick={() => ref.current?.click()}
      style={{ border: `2px dashed ${C.brand}`, borderRadius: 10, padding: '24px 32px', textAlign: 'center', cursor: 'pointer', color: C.brand, fontSize: 14 }}
    >
      {label ?? `Arrastra o haz clic para subir ${accept.toUpperCase()}`}
      <input ref={ref} type="file" accept={accept} style={{ display: 'none' }} onChange={e => { if (e.target.files?.[0]) onFile(e.target.files[0]); }} />
    </div>
  );
}

function ErrorPanel({ mensaje }: { mensaje: string }) {
  return <div style={{ background: '#fee2e2', border: '1px solid #fca5a5', borderRadius: 8, padding: 12, color: C.danger, fontSize: 13 }}>{mensaje}</div>;
}

// ── Tab Mensajería ────────────────────────────────────────────────────────────

function TabMensajeria() {
  const [etapa, setEtapa]         = useState<'carga' | 'preview' | 'exito'>('carga');
  const [preview, setPreview]     = useState<ValidarMensajeriaResponse | null>(null);
  const [desde, setDesde]         = useState(HACE30);
  const [hasta, setHasta]         = useState(HOY);

  const validar  = useMutation({ mutationFn: validarMensajeria, onSuccess: r => { setPreview(r); setEtapa('preview'); } });
  const confirmar= useMutation({ mutationFn: (f: FilaMensajeria[]) => confirmarMensajeria(f), onSuccess: () => setEtapa('exito') });

  const { data: informe, refetch } = useQuery({
    queryKey: ['informe-mensajeria', desde, hasta],
    queryFn: () => getInformeMensajeria(desde, hasta),
    enabled: false,
  });

  return (
    <div style={{ display: 'flex', flexDirection: 'column', gap: 20 }}>
      <h3 style={{ margin: 0, color: C.mensajes }}>Mensajería</h3>

      {etapa === 'carga' && (
        <>
          <DropZone onFile={f => validar.mutate(f)} label="Subir Excel de mensajería (.xlsx)" />
          {validar.isPending && <p style={{ color: C.gray }}>Validando…</p>}
          {validar.isError && <ErrorPanel mensaje="Error al validar el archivo." />}
        </>
      )}

      {etapa === 'preview' && preview && (
        <>
          <div style={{ display: 'flex', gap: 12, flexWrap: 'wrap' }}>
            <KpiCard label="Total filas"  value={preview.totalFilas}   />
            <KpiCard label="Válidas"      value={preview.totalValidas} color={C.success} />
            <KpiCard label="Con errores"  value={preview.totalErrores} color={C.danger}  />
            <KpiCard label="Alertas >4"   value={preview.alertas.length} color={C.warning} />
          </div>

          {preview.alertas.length > 0 && (
            <div style={{ background: '#fffbeb', border: `1px solid ${C.warning}`, borderRadius: 8, padding: 12 }}>
              <strong style={{ color: C.warning }}>⚠ Líneas con más de 4 mensajes en esta carga:</strong>
              <table style={{ width: '100%', fontSize: 12, marginTop: 8, borderCollapse: 'collapse' }}>
                <thead><tr style={{ color: C.gray }}><th style={{ textAlign: 'left' }}>Teléfono</th><th>Cartera</th><th>Total</th></tr></thead>
                <tbody>{preview.alertas.map((a, i) => (
                  <tr key={i}><td>{a.numeroTelefono}</td><td>{a.nombreCartera}</td><td style={{ color: C.danger, fontWeight: 700 }}>{a.totalMensajes}</td></tr>
                ))}</tbody>
              </table>
            </div>
          )}

          <table style={{ fontSize: 12, borderCollapse: 'collapse', width: '100%' }}>
            <thead style={{ background: 'var(--z-bg-subtle)' }}>
              <tr>{['Documento','Teléfono','Cartera','Fecha','Estado'].map(h => <th key={h} style={{ padding: '6px 10px', textAlign: 'left', color: C.gray }}>{h}</th>)}</tr>
            </thead>
            <tbody>{preview.preview.map((f, i) => (
              <tr key={i} style={{ borderTop: '1px solid var(--z-border)' }}>
                <td style={{ padding: '5px 10px' }}>{f.numeroDocumento}</td>
                <td style={{ padding: '5px 10px' }}>{f.numeroTelefono}</td>
                <td style={{ padding: '5px 10px' }}>{f.nombreCartera}</td>
                <td style={{ padding: '5px 10px' }}>{f.fechaEnvio}</td>
                <td style={{ padding: '5px 10px', color: f.esValida ? C.success : C.danger }}>{f.esValida ? 'Válida' : f.errorMensaje}</td>
              </tr>
            ))}</tbody>
          </table>

          <div style={{ display: 'flex', gap: 10 }}>
            <button disabled={preview.totalValidas === 0 || confirmar.isPending}
              onClick={() => confirmar.mutate(preview.filasValidas)}
              style={{ background: C.brand, color: '#fff', border: 'none', borderRadius: 6, padding: '8px 20px', cursor: 'pointer' }}>
              Confirmar {preview.totalValidas} registro(s)
            </button>
            <button onClick={() => setEtapa('carga')}
              style={{ background: 'transparent', border: `1px solid ${C.gray}`, borderRadius: 6, padding: '8px 16px', cursor: 'pointer', color: C.gray }}>
              Cancelar
            </button>
          </div>
        </>
      )}

      {etapa === 'exito' && (
        <div style={{ background: '#dcfce7', border: `1px solid ${C.success}`, borderRadius: 8, padding: 16, color: C.success }}>
          ✓ Mensajes registrados correctamente.
          <button onClick={() => { setEtapa('carga'); setPreview(null); }}
            style={{ marginLeft: 16, background: C.success, color: '#fff', border: 'none', borderRadius: 6, padding: '6px 14px', cursor: 'pointer' }}>
            Nueva carga
          </button>
        </div>
      )}

      {/* Sección informe */}
      <div style={{ borderTop: '1px solid var(--z-border)', paddingTop: 20 }}>
        <h4 style={{ margin: '0 0 12px' }}>Informe de mensajería enviada</h4>
        <SelectorPeriodo onChange={(d, h) => { setDesde(d); setHasta(h); refetch(); }} />
        {informe && (
          <>
            <KpiCard label="Total mensajes" value={informe.totalMensajes} color={C.mensajes} />
            <div style={{ marginTop: 16, height: 220 }}>
              <ResponsiveContainer width="100%" height="100%">
                <BarChart data={informe.porCartera} layout="vertical">
                  <XAxis type="number" tick={{ fontSize: 11 }} />
                  <YAxis dataKey="nombreCartera" type="category" width={120} tick={{ fontSize: 11 }} />
                  <Tooltip />
                  <Bar dataKey="total" fill={C.mensajes} radius={[0, 4, 4, 0]} />
                </BarChart>
              </ResponsiveContainer>
            </div>
            {informe.alertas.length > 0 && (
              <div style={{ marginTop: 12 }}>
                <strong style={{ color: C.warning, fontSize: 13 }}>Alertas: líneas con &gt;4 mensajes en el período</strong>
                <table style={{ width: '100%', fontSize: 12, marginTop: 6, borderCollapse: 'collapse' }}>
                  <thead><tr style={{ color: C.gray }}><th style={{ textAlign: 'left' }}>Teléfono</th><th>Cartera</th><th>Total</th></tr></thead>
                  <tbody>{informe.alertas.map((a, i) => (
                    <tr key={i}><td>{a.numeroTelefono}</td><td>{a.nombreCartera}</td><td style={{ color: C.danger, fontWeight: 700 }}>{a.totalMensajes}</td></tr>
                  ))}</tbody>
                </table>
              </div>
            )}
          </>
        )}
      </div>
    </div>
  );
}

// ── Tab IVR ───────────────────────────────────────────────────────────────────

function TabIVR() {
  const [subTab, setSubTab]       = useState<'pre' | 'post' | 'informe'>('pre');
  const [previewPre, setPreviewPre]   = useState<ValidarIVRResponse | null>(null);
  const [previewPost, setPreviewPost] = useState<ValidarIVRResponse | null>(null);
  const [etapaPre, setEtapaPre]   = useState<'carga' | 'preview' | 'exito'>('carga');
  const [etapaPost, setEtapaPost] = useState<'carga' | 'preview' | 'exito'>('carga');
  const [desde, setDesde]         = useState(HACE30);
  const [hasta, setHasta]         = useState(HOY);

  const validarPre   = useMutation({ mutationFn: validarIVRPreEnvio,   onSuccess: r => { setPreviewPre(r); setEtapaPre('preview'); } });
  const confirmarPre = useMutation({ mutationFn: (f: FilaIVR[]) => confirmarIVRPreEnvio(f), onSuccess: () => setEtapaPre('exito') });
  const validarPost  = useMutation({ mutationFn: validarIVRPostEnvio,  onSuccess: r => { setPreviewPost(r); setEtapaPost('preview'); } });
  const confirmarPost= useMutation({ mutationFn: (f: FilaIVR[]) => confirmarIVRPostEnvio(f), onSuccess: () => setEtapaPost('exito') });

  const { data: informe, refetch } = useQuery({
    queryKey: ['informe-ivr', desde, hasta],
    queryFn: () => getInformeIVR(desde, hasta),
    enabled: false,
  });

  const PIES = [{ name: 'Contestaron', value: informe?.totalContestaron ?? 0 }, { name: 'No contestaron', value: informe?.totalNoContestaron ?? 0 }];
  const PIE_COLORS = [C.success, C.danger];

  return (
    <div style={{ display: 'flex', flexDirection: 'column', gap: 20 }}>
      <h3 style={{ margin: 0, color: C.ivr }}>IVR</h3>

      <div style={{ display: 'flex', gap: 8 }}>
        {(['pre', 'post', 'informe'] as const).map(t => (
          <button key={t} onClick={() => setSubTab(t)}
            style={{ padding: '5px 14px', borderRadius: 6, border: 'none', cursor: 'pointer', fontSize: 13,
              background: subTab === t ? C.ivr : 'var(--z-bg-subtle)', color: subTab === t ? '#fff' : C.gray }}>
            {t === 'pre' ? 'Pre-envío' : t === 'post' ? 'Post-envío' : 'Informe'}
          </button>
        ))}
      </div>

      {subTab === 'pre' && (
        <>
          {etapaPre === 'carga' && (
            <>
              <DropZone onFile={f => validarPre.mutate(f)} label="Subir Excel pre-envío IVR (.xlsx)" />
              {validarPre.isPending && <p style={{ color: C.gray }}>Validando…</p>}
            </>
          )}
          {etapaPre === 'preview' && previewPre && (
            <>
              <div style={{ display: 'flex', gap: 12 }}>
                <KpiCard label="Total"   value={previewPre.totalFilas}   />
                <KpiCard label="Válidas" value={previewPre.totalValidas} color={C.success} />
                <KpiCard label="Errores" value={previewPre.totalErrores} color={C.danger}  />
              </div>
              <div style={{ display: 'flex', gap: 10, marginTop: 8 }}>
                <button disabled={previewPre.totalValidas === 0 || confirmarPre.isPending}
                  onClick={() => confirmarPre.mutate(previewPre.filasValidas)}
                  style={{ background: C.ivr, color: '#fff', border: 'none', borderRadius: 6, padding: '8px 20px', cursor: 'pointer' }}>
                  Confirmar {previewPre.totalValidas} pre-envío(s)
                </button>
                <button onClick={() => setEtapaPre('carga')} style={{ background: 'transparent', border: `1px solid ${C.gray}`, borderRadius: 6, padding: '8px 16px', cursor: 'pointer', color: C.gray }}>Cancelar</button>
              </div>
            </>
          )}
          {etapaPre === 'exito' && (
            <div style={{ background: '#dcfce7', border: `1px solid ${C.success}`, borderRadius: 8, padding: 16, color: C.success }}>
              ✓ Pre-envíos registrados.
              <button onClick={() => { setEtapaPre('carga'); setPreviewPre(null); }} style={{ marginLeft: 12, background: C.success, color: '#fff', border: 'none', borderRadius: 6, padding: '6px 12px', cursor: 'pointer' }}>Nuevo</button>
            </div>
          )}
        </>
      )}

      {subTab === 'post' && (
        <>
          {etapaPost === 'carga' && (
            <>
              <DropZone onFile={f => validarPost.mutate(f)} label="Subir Excel post-envío con resultados (.xlsx)" />
              {validarPost.isPending && <p style={{ color: C.gray }}>Validando…</p>}
            </>
          )}
          {etapaPost === 'preview' && previewPost && (
            <>
              <div style={{ display: 'flex', gap: 12 }}>
                <KpiCard label="Total"      value={previewPost.totalFilas}   />
                <KpiCard label="Actualizará" value={previewPost.totalValidas} color={C.success} />
                <KpiCard label="Sin match"  value={previewPost.totalErrores} color={C.danger}  />
              </div>
              <div style={{ display: 'flex', gap: 10, marginTop: 8 }}>
                <button disabled={previewPost.totalValidas === 0 || confirmarPost.isPending}
                  onClick={() => confirmarPost.mutate(previewPost.filasValidas)}
                  style={{ background: C.ivr, color: '#fff', border: 'none', borderRadius: 6, padding: '8px 20px', cursor: 'pointer' }}>
                  Confirmar {previewPost.totalValidas} resultado(s)
                </button>
                <button onClick={() => setEtapaPost('carga')} style={{ background: 'transparent', border: `1px solid ${C.gray}`, borderRadius: 6, padding: '8px 16px', cursor: 'pointer', color: C.gray }}>Cancelar</button>
              </div>
            </>
          )}
          {etapaPost === 'exito' && (
            <div style={{ background: '#dcfce7', border: `1px solid ${C.success}`, borderRadius: 8, padding: 16, color: C.success }}>
              ✓ Resultados actualizados.
              <button onClick={() => { setEtapaPost('carga'); setPreviewPost(null); }} style={{ marginLeft: 12, background: C.success, color: '#fff', border: 'none', borderRadius: 6, padding: '6px 12px', cursor: 'pointer' }}>Nuevo</button>
            </div>
          )}
        </>
      )}

      {subTab === 'informe' && (
        <>
          <SelectorPeriodo onChange={(d, h) => { setDesde(d); setHasta(h); refetch(); }} />
          {informe && (
            <>
              <div style={{ display: 'flex', gap: 12, flexWrap: 'wrap' }}>
                <KpiCard label="Solicitados"   value={informe.totalSolicitados}   color={C.ivr}     />
                <KpiCard label="Contestaron"   value={`${informe.totalContestaron} (${informe.pctContesto}%)`}    color={C.success} />
                <KpiCard label="No contestaron" value={`${informe.totalNoContestaron} (${informe.pctNoContesto}%)`} color={C.danger}  />
              </div>
              <div style={{ display: 'flex', gap: 20, marginTop: 8, flexWrap: 'wrap' }}>
                <div style={{ flex: 2, minWidth: 260, height: 200 }}>
                  <p style={{ fontSize: 13, color: C.gray, margin: '0 0 6px' }}>IVR solicitados por semana</p>
                  <ResponsiveContainer width="100%" height="100%">
                    <BarChart data={informe.porSemana}>
                      <XAxis dataKey="semana" tick={{ fontSize: 11 }} label={{ value: 'Semana', position: 'insideBottom', offset: -2, fontSize: 11 }} />
                      <YAxis tick={{ fontSize: 11 }} />
                      <Tooltip />
                      <Bar dataKey="total" fill={C.ivr} name="Solicitados" radius={[4, 4, 0, 0]} />
                      <Bar dataKey="contestaron" fill={C.success} name="Contestaron" radius={[4, 4, 0, 0]} />
                    </BarChart>
                  </ResponsiveContainer>
                </div>
                <div style={{ flex: 1, minWidth: 200, height: 200 }}>
                  <p style={{ fontSize: 13, color: C.gray, margin: '0 0 6px' }}>Resultado global</p>
                  <ResponsiveContainer width="100%" height="100%">
                    <PieChart>
                      <Pie data={PIES} dataKey="value" nameKey="name" cx="50%" cy="50%" outerRadius={70} label={({ name, percent }) => `${name} ${(percent * 100).toFixed(0)}%`}>
                        {PIES.map((_, i) => <Cell key={i} fill={PIE_COLORS[i]} />)}
                      </Pie>
                      <Legend />
                    </PieChart>
                  </ResponsiveContainer>
                </div>
              </div>
              {informe.alertas.length > 0 && (
                <div style={{ background: '#fffbeb', border: `1px solid ${C.warning}`, borderRadius: 8, padding: 12, marginTop: 8 }}>
                  <strong style={{ color: C.warning, fontSize: 13 }}>⚠ Líneas con más de 5 IVR</strong>
                  <table style={{ width: '100%', fontSize: 12, marginTop: 6, borderCollapse: 'collapse' }}>
                    <thead><tr style={{ color: C.gray }}><th style={{ textAlign: 'left' }}>Celular</th><th>Cartera</th><th>Total</th></tr></thead>
                    <tbody>{informe.alertas.map((a, i) => (
                      <tr key={i}><td>{a.celular}</td><td>{a.nombreCartera}</td><td style={{ color: C.danger, fontWeight: 700 }}>{a.totalIVR}</td></tr>
                    ))}</tbody>
                  </table>
                </div>
              )}
              {informe.vsGestion.length > 0 && (
                <div style={{ marginTop: 12 }}>
                  <strong style={{ fontSize: 13 }}>IVR Contestado vs Gestión</strong>
                  <table style={{ width: '100%', fontSize: 12, marginTop: 6, borderCollapse: 'collapse' }}>
                    <thead><tr style={{ color: C.gray }}>{['TitularID','Celular','Resultado','Último Perfil','Últ. Gestión','Promesa'].map(h => <th key={h} style={{ textAlign: 'left', padding: '4px 8px' }}>{h}</th>)}</tr></thead>
                    <tbody>{informe.vsGestion.map((r, i) => (
                      <tr key={i} style={{ borderTop: '1px solid var(--z-border)' }}>
                        <td style={{ padding: '4px 8px' }}>{r.titularID}</td>
                        <td style={{ padding: '4px 8px' }}>{r.celular}</td>
                        <td style={{ padding: '4px 8px', color: C.success }}>{r.resultado}</td>
                        <td style={{ padding: '4px 8px' }}>{r.ultimoPerfil ?? '—'}</td>
                        <td style={{ padding: '4px 8px' }}>{r.fechaUltimaGestion ?? '—'}</td>
                        <td style={{ padding: '4px 8px' }}>{r.fechaPromesa ?? '—'}</td>
                      </tr>
                    ))}</tbody>
                  </table>
                </div>
              )}
            </>
          )}
        </>
      )}
    </div>
  );
}

// ── Tab Llamadas Perdidas ─────────────────────────────────────────────────────

function TabLlamadasPerdidas() {
  const [etapa, setEtapa]     = useState<'carga' | 'preview' | 'exito'>('carga');
  const [preview, setPreview] = useState<ValidarLlamadasResponse | null>(null);

  const validar   = useMutation({ mutationFn: validarLlamadasPerdidas, onSuccess: r => { setPreview(r); setEtapa('preview'); } });
  const confirmar = useMutation({ mutationFn: (f: FilaLlamada[]) => confirmarLlamadasPerdidas(f), onSuccess: () => setEtapa('exito') });

  const { data: informe, refetch } = useQuery({
    queryKey: ['informe-llamadas'],
    queryFn: getInformeLlamadasPerdidas,
    enabled: false,
  });

  return (
    <div style={{ display: 'flex', flexDirection: 'column', gap: 20 }}>
      <h3 style={{ margin: 0, color: C.llamadas }}>Llamadas Perdidas</h3>

      {etapa === 'carga' && (
        <>
          <p style={{ fontSize: 13, color: C.gray, margin: 0 }}>Formato esperado: TitularID, Celular, Cartera. Solo se conservan clientes vigentes.</p>
          <DropZone onFile={f => validar.mutate(f)} label="Subir Excel de llamadas perdidas (.xlsx)" />
          {validar.isPending && <p style={{ color: C.gray }}>Validando…</p>}
        </>
      )}

      {etapa === 'preview' && preview && (
        <>
          <div style={{ display: 'flex', gap: 12 }}>
            <KpiCard label="Total filas"  value={preview.totalFilas}     />
            <KpiCard label="Vigentes"     value={preview.totalValidas}   color={C.success} />
            <KpiCard label="Descartadas"  value={preview.totalInvalidas} color={C.danger}  />
          </div>
          <table style={{ fontSize: 12, borderCollapse: 'collapse', width: '100%' }}>
            <thead style={{ background: 'var(--z-bg-subtle)' }}>
              <tr>{['TitularID','Celular','Cartera','Estado'].map(h => <th key={h} style={{ padding: '6px 10px', textAlign: 'left', color: C.gray }}>{h}</th>)}</tr>
            </thead>
            <tbody>{preview.preview.map((f, i) => (
              <tr key={i} style={{ borderTop: '1px solid var(--z-border)' }}>
                <td style={{ padding: '5px 10px' }}>{f.titularID}</td>
                <td style={{ padding: '5px 10px' }}>{f.celular}</td>
                <td style={{ padding: '5px 10px' }}>{f.nombreCartera}</td>
                <td style={{ padding: '5px 10px', color: f.estado === 'Válida' ? C.success : C.danger }}>{f.estado}</td>
              </tr>
            ))}</tbody>
          </table>
          <div style={{ display: 'flex', gap: 10 }}>
            <button disabled={preview.totalValidas === 0 || confirmar.isPending}
              onClick={() => confirmar.mutate(preview.filasValidas)}
              style={{ background: C.llamadas, color: '#fff', border: 'none', borderRadius: 6, padding: '8px 20px', cursor: 'pointer' }}>
              Confirmar {preview.totalValidas} llamada(s)
            </button>
            <button onClick={() => setEtapa('carga')} style={{ background: 'transparent', border: `1px solid ${C.gray}`, borderRadius: 6, padding: '8px 16px', cursor: 'pointer', color: C.gray }}>Cancelar</button>
          </div>
        </>
      )}

      {etapa === 'exito' && (
        <div style={{ background: '#dcfce7', border: `1px solid ${C.success}`, borderRadius: 8, padding: 16, color: C.success }}>
          ✓ Llamadas perdidas registradas.
          <button onClick={() => { setEtapa('carga'); setPreview(null); }} style={{ marginLeft: 12, background: C.success, color: '#fff', border: 'none', borderRadius: 6, padding: '6px 12px', cursor: 'pointer' }}>Nueva carga</button>
        </div>
      )}

      <div style={{ borderTop: '1px solid var(--z-border)', paddingTop: 20 }}>
        <h4 style={{ margin: '0 0 12px' }}>Informe histórico</h4>
        <button onClick={() => refetch()} style={{ background: C.llamadas, color: '#fff', border: 'none', borderRadius: 6, padding: '6px 14px', cursor: 'pointer', fontSize: 13 }}>Consultar</button>
        {informe && (
          <>
            <div style={{ marginTop: 12, height: 200 }}>
              <ResponsiveContainer width="100%" height="100%">
                <BarChart data={informe.porCartera} layout="vertical">
                  <XAxis type="number" tick={{ fontSize: 11 }} />
                  <YAxis dataKey="nombreCartera" type="category" width={120} tick={{ fontSize: 11 }} />
                  <Tooltip />
                  <Bar dataKey="total" fill={C.llamadas} radius={[0, 4, 4, 0]} />
                </BarChart>
              </ResponsiveContainer>
            </div>
            {informe.alertas.length > 0 && (
              <div style={{ background: '#fffbeb', border: `1px solid ${C.warning}`, borderRadius: 8, padding: 12, marginTop: 12 }}>
                <strong style={{ color: C.warning, fontSize: 13 }}>⚠ Misma línea comunicada múltiples veces</strong>
                <table style={{ width: '100%', fontSize: 12, marginTop: 6, borderCollapse: 'collapse' }}>
                  <thead><tr style={{ color: C.gray }}><th style={{ textAlign: 'left' }}>Celular</th><th>Veces comunicado</th></tr></thead>
                  <tbody>{informe.alertas.map((a, i) => (
                    <tr key={i}><td>{a.celular}</td><td style={{ color: C.danger, fontWeight: 700 }}>{a.vecesComunicado}</td></tr>
                  ))}</tbody>
                </table>
              </div>
            )}
          </>
        )}
      </div>
    </div>
  );
}

// ── Tab Llamadas Entrantes ────────────────────────────────────────────────────

function TabLlamadasEntrantes() {
  const [etapa, setEtapa]     = useState<'carga' | 'preview' | 'exito'>('carga');
  const [preview, setPreview] = useState<ValidarEntrantesResponse | null>(null);
  const [anio, setAnio]       = useState(AÑO_HOY);

  const validar   = useMutation({ mutationFn: validarLlamadasEntrantes, onSuccess: r => { setPreview(r); setEtapa('preview'); } });
  const confirmar = useMutation({ mutationFn: () => confirmarLlamadasEntrantes(preview!.filasParaGuardar), onSuccess: () => setEtapa('exito') });

  const { data: informe, refetch } = useQuery({
    queryKey: ['informe-entrantes', anio],
    queryFn: () => getInformeEntrantes(anio),
    enabled: false,
  });

  return (
    <div style={{ display: 'flex', flexDirection: 'column', gap: 20 }}>
      <h3 style={{ margin: 0, color: C.entrantes }}>Llamadas Entrantes</h3>

      {etapa === 'carga' && (
        <>
          <p style={{ fontSize: 13, color: C.gray, margin: 0 }}>Sube el archivo semanal con celulares (una columna). El sistema resuelve titulares y detecta acuerdos de pago.</p>
          <DropZone onFile={f => validar.mutate(f)} label="Subir Excel de llamadas entrantes (.xlsx)" />
          {validar.isPending && <p style={{ color: C.gray }}>Procesando…</p>}
        </>
      )}

      {etapa === 'preview' && preview && (
        <>
          <div style={{ display: 'flex', gap: 12, flexWrap: 'wrap' }}>
            <KpiCard label="Celulares"     value={preview.totalCelulares}  />
            <KpiCard label="Resueltos"     value={preview.totalResueltos}  color={C.brand}    />
            <KpiCard label="Con acuerdo"   value={preview.totalConAcuerdo} color={C.success}  />
            <KpiCard label="Efectividad"   value={`${preview.efectividad}%`} color={C.entrantes} />
          </div>
          <div style={{ display: 'flex', gap: 10 }}>
            <button disabled={confirmar.isPending} onClick={() => confirmar.mutate()}
              style={{ background: C.entrantes, color: '#fff', border: 'none', borderRadius: 6, padding: '8px 20px', cursor: 'pointer' }}>
              Guardar {preview.totalCelulares} registro(s)
            </button>
            <button onClick={() => setEtapa('carga')} style={{ background: 'transparent', border: `1px solid ${C.gray}`, borderRadius: 6, padding: '8px 16px', cursor: 'pointer', color: C.gray }}>Cancelar</button>
          </div>
        </>
      )}

      {etapa === 'exito' && (
        <div style={{ background: '#dcfce7', border: `1px solid ${C.success}`, borderRadius: 8, padding: 16, color: C.success }}>
          ✓ Llamadas entrantes guardadas.
          <button onClick={() => { setEtapa('carga'); setPreview(null); }} style={{ marginLeft: 12, background: C.success, color: '#fff', border: 'none', borderRadius: 6, padding: '6px 12px', cursor: 'pointer' }}>Nueva carga</button>
        </div>
      )}

      <div style={{ borderTop: '1px solid var(--z-border)', paddingTop: 20 }}>
        <h4 style={{ margin: '0 0 12px' }}>Efectividad por semana</h4>
        <div style={{ display: 'flex', gap: 10, alignItems: 'center', marginBottom: 12 }}>
          <label style={{ fontSize: 13 }}>Año <input type="number" value={anio} min={2024} max={2099} onChange={e => setAnio(+e.target.value)} style={{ width: 70, marginLeft: 4 }} /></label>
          <button onClick={() => refetch()} style={{ background: C.entrantes, color: '#fff', border: 'none', borderRadius: 6, padding: '6px 14px', cursor: 'pointer', fontSize: 13 }}>Consultar</button>
        </div>
        {informe && (
          <div style={{ height: 220 }}>
            <ResponsiveContainer width="100%" height="100%">
              <BarChart data={informe.porSemana}>
                <XAxis dataKey="semana" tick={{ fontSize: 11 }} />
                <YAxis tick={{ fontSize: 11 }} />
                <Tooltip formatter={(val, name) => [name === 'efectividad' ? `${val}%` : val, name]} />
                <Bar dataKey="totalCelulares" fill={C.gray}      name="Total"       radius={[4, 4, 0, 0]} />
                <Bar dataKey="resueltos"      fill={C.brand}     name="Resueltos"   radius={[4, 4, 0, 0]} />
                <Bar dataKey="conAcuerdo"     fill={C.success}   name="Con acuerdo" radius={[4, 4, 0, 0]} />
              </BarChart>
            </ResponsiveContainer>
          </div>
        )}
      </div>
    </div>
  );
}

// ── Tab Pagos vs Impacto ──────────────────────────────────────────────────────

function TabPagosImpacto() {
  const [desde, setDesde] = useState(HACE30);
  const [hasta, setHasta] = useState(HOY);

  const { data: informe, refetch, isFetching } = useQuery({
    queryKey: ['pagos-impacto', desde, hasta],
    queryFn: () => getInformePagosImpacto(desde, hasta),
    enabled: false,
  });

  const barData = informe ? [
    { canal: 'Mensajería',      pagos: informe.resumen.pagosConMensajeria },
    { canal: 'IVR',             pagos: informe.resumen.pagosConIVR        },
    { canal: 'Llamada perdida', pagos: informe.resumen.pagosConLlamada    },
    { canal: 'Sin impacto',     pagos: informe.resumen.pagosSinImpacto    },
  ] : [];

  return (
    <div style={{ display: 'flex', flexDirection: 'column', gap: 20 }}>
      <h3 style={{ margin: 0, color: C.impacto }}>Pagos vs Canal de Impacto</h3>
      <p style={{ fontSize: 13, color: C.gray, margin: 0 }}>De los titulares que pagaron en el período, ¿por qué canal de virtualidad fueron impactados previamente?</p>

      <SelectorPeriodo onChange={(d, h) => { setDesde(d); setHasta(h); refetch(); }} />

      {isFetching && <p style={{ color: C.gray }}>Calculando…</p>}

      {informe && (
        <>
          <div style={{ display: 'flex', gap: 12, flexWrap: 'wrap' }}>
            <KpiCard label="Total pagos"       value={informe.resumen.totalPagos}         />
            <KpiCard label="Con mensajería"    value={informe.resumen.pagosConMensajeria} color={C.mensajes}  />
            <KpiCard label="Con IVR"           value={informe.resumen.pagosConIVR}        color={C.ivr}       />
            <KpiCard label="Con llamada"       value={informe.resumen.pagosConLlamada}    color={C.llamadas}  />
            <KpiCard label="Sin impacto"       value={informe.resumen.pagosSinImpacto}    color={C.gray}      />
          </div>

          <div style={{ height: 220, marginTop: 8 }}>
            <ResponsiveContainer width="100%" height="100%">
              <BarChart data={barData}>
                <XAxis dataKey="canal" tick={{ fontSize: 12 }} />
                <YAxis tick={{ fontSize: 11 }} />
                <Tooltip />
                <Bar dataKey="pagos" fill={C.impacto} radius={[4, 4, 0, 0]} />
              </BarChart>
            </ResponsiveContainer>
          </div>

          <table style={{ fontSize: 12, borderCollapse: 'collapse', width: '100%', marginTop: 8 }}>
            <thead style={{ background: 'var(--z-bg-subtle)' }}>
              <tr>{['Cédula','Fecha pago','Valor','Mensajería','IVR','Llamada'].map(h => <th key={h} style={{ padding: '6px 10px', textAlign: 'left', color: C.gray }}>{h}</th>)}</tr>
            </thead>
            <tbody>{informe.filas.slice(0, 50).map((f, i) => (
              <tr key={i} style={{ borderTop: '1px solid var(--z-border)' }}>
                <td style={{ padding: '5px 10px' }}>{f.cedulaTitular}</td>
                <td style={{ padding: '5px 10px' }}>{f.fechaPago}</td>
                <td style={{ padding: '5px 10px' }}>${f.valorPagado.toLocaleString('es-CO')}</td>
                <td style={{ padding: '5px 10px', color: f.impactadoMensajeria ? C.success : C.gray }}>{f.impactadoMensajeria ? '✓' : '—'}</td>
                <td style={{ padding: '5px 10px', color: f.impactadoIVR        ? C.success : C.gray }}>{f.impactadoIVR        ? '✓' : '—'}</td>
                <td style={{ padding: '5px 10px', color: f.impactadoLlamada    ? C.success : C.gray }}>{f.impactadoLlamada    ? '✓' : '—'}</td>
              </tr>
            ))}</tbody>
          </table>
        </>
      )}
    </div>
  );
}

// ── Página principal ──────────────────────────────────────────────────────────

const TABS: { key: Tab; label: string; color: string }[] = [
  { key: 'mensajeria',        label: 'Mensajería',       color: C.mensajes  },
  { key: 'ivr',               label: 'IVR',              color: C.ivr       },
  { key: 'llamadas-perdidas', label: 'Llamadas Perdidas',color: C.llamadas  },
  { key: 'llamadas-entrantes',label: 'Llamadas Entrantes',color: C.entrantes},
  { key: 'pagos-impacto',     label: 'Pagos vs Impacto', color: C.impacto   },
];

export function InformesVirtualidadPage() {
  const [tab, setTab] = useState<Tab>('mensajeria');

  return (
    <div style={{ padding: '24px 28px', display: 'flex', flexDirection: 'column', gap: 20 }}>
      <h2 style={{ margin: 0 }}>Informes de Virtualidad</h2>

      {/* Tabs */}
      <div style={{ display: 'flex', gap: 6, flexWrap: 'wrap', borderBottom: '2px solid var(--z-border)', paddingBottom: 4 }}>
        {TABS.map(t => (
          <button key={t.key} onClick={() => setTab(t.key)}
            style={{
              padding: '6px 16px', borderRadius: '6px 6px 0 0', border: 'none', cursor: 'pointer', fontSize: 13,
              background: tab === t.key ? t.color : 'transparent',
              color:      tab === t.key ? '#fff' : C.gray,
              fontWeight: tab === t.key ? 600 : 400,
              borderBottom: tab === t.key ? `2px solid ${t.color}` : '2px solid transparent',
            }}>
            {t.label}
          </button>
        ))}
      </div>

      {/* Contenido */}
      <div style={{ background: 'var(--z-bg-card)', border: '1px solid var(--z-border)', borderRadius: 10, padding: 24 }}>
        {tab === 'mensajeria'         && <TabMensajeria />}
        {tab === 'ivr'                && <TabIVR />}
        {tab === 'llamadas-perdidas'  && <TabLlamadasPerdidas />}
        {tab === 'llamadas-entrantes' && <TabLlamadasEntrantes />}
        {tab === 'pagos-impacto'      && <TabPagosImpacto />}
      </div>
    </div>
  );
}
