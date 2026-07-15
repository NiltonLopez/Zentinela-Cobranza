/* eslint-disable react-refresh/only-export-components */
// Módulo de utilidades compartidas: exporta a propósito componentes + helpers/tipos
// juntos (paleta, formateadores, PDF, ordenamiento). El HMR de fast-refresh no aplica.
// ╔══════════════════════════════════════════════════════════════════════════════╗
// ║  Reporte compartido — piezas reutilizables de los informes de recaudo         ║
// ║  Usado por InformesRecaudoPage y AgenciasExternasPage para no duplicar:        ║
// ║  KPIs, semáforo, chips, ordenamiento de tablas, filtros, y la descarga PDF     ║
// ║  con su modal de selección de columnas.                                        ║
// ╚══════════════════════════════════════════════════════════════════════════════╝
import { useState, useMemo } from 'react';

// ── Paleta ────────────────────────────────────────────────────────────────────
export const C = {
  brand:      '#2563eb',
  meta:       '#94a3b8',
  proyeccion: '#f59e0b',
  honorarios: '#7c3aed',
  verde:      '#16a34a',
  rojo:       '#dc2626',
  naranja:    '#ea580c',
  mesA:       '#94a3b8',
  mesB:       '#2563eb',
  mesC:       '#10b981',
};

// ── Helpers ───────────────────────────────────────────────────────────────────
export const cop = (v: number) =>
  new Intl.NumberFormat('es-CO', {
    style: 'currency', currency: 'COP', minimumFractionDigits: 0,
  }).format(v);

export const MESES = [
  'Enero', 'Febrero', 'Marzo', 'Abril', 'Mayo', 'Junio',
  'Julio', 'Agosto', 'Septiembre', 'Octubre', 'Noviembre', 'Diciembre',
];

// ── KPI card ──────────────────────────────────────────────────────────────────
export function KpiCard({ label, value, valueSuffix, sub, color }: {
  label: string; value: React.ReactNode; valueSuffix?: string; sub?: string; color?: string;
}) {
  return (
    <div style={{
      background: 'var(--z-bg-card)', border: '1px solid var(--z-border)',
      borderRadius: 10, padding: '16px 20px', flex: 1, minWidth: 160,
      boxShadow: '0 4px 12px rgba(0,0,0,0.08), 0 1px 3px rgba(0,0,0,0.05)',
    }}>
      <p style={{ fontSize: 11, color: 'var(--z-text-muted)', marginBottom: 4, textTransform: 'uppercase', letterSpacing: '0.05em' }}>
        {label}
      </p>
      <p style={{ fontSize: 22, fontWeight: 600, color: color ?? 'var(--z-text-primary)', margin: 0 }}>
        {value}
        {valueSuffix && (
          <span style={{ fontSize: 16, fontWeight: 400, color: '#94a3b8', marginLeft: 4 }}>
            {valueSuffix}
          </span>
        )}
      </p>
      {sub && <p style={{ fontSize: 12, color: 'var(--z-text-muted)', marginTop: 2 }}>{sub}</p>}
    </div>
  );
}

// ── Semáforo de cumplimiento ──────────────────────────────────────────────────
// Colorimetría: ≥100% verde · ≥90% amarillo · <90% rojo (basado en % vs meta total)
export function Semaforo({ valorActual, valorMeta }: {
  valorActual: number; valorMeta: number;
}) {
  if (valorMeta === 0) return <span style={{ color: 'var(--z-text-muted)', fontSize: 12 }}>—</span>;
  const pctCump = (valorActual / valorMeta) * 100;
  const color   = pctCump >= 100 ? '#16a34a' : pctCump >= 90 ? '#ca8a04' : '#dc2626';
  return (
    <span style={{ color, fontWeight: 600, fontSize: 13 }}>{pctCump.toFixed(1)}%</span>
  );
}

export function ChipTipoMeta({ tipo }: { tipo: 'Recaudo' | 'Honorarios' | null }) {
  if (!tipo) return <span style={{ color: 'var(--z-text-muted)', fontSize: 11 }}>—</span>;
  const color = tipo === 'Honorarios' ? C.honorarios : C.brand;
  return (
    <span style={{
      fontSize: 10, fontWeight: 700, padding: '2px 6px', borderRadius: 4,
      background: `${color}22`, color, whiteSpace: 'nowrap',
    }}>
      {tipo}
    </span>
  );
}

export function Cargando() {
  return (
    <div style={{ textAlign: 'center', padding: '48px 0', color: 'var(--z-text-muted)', fontSize: 13 }}>
      Cargando datos…
    </div>
  );
}

export function ErrorMsg() {
  return (
    <div style={{ textAlign: 'center', padding: '48px 0', color: C.rojo, fontSize: 13 }}>
      Error al cargar el informe. Verifica la conexión e inténtalo de nuevo.
    </div>
  );
}

// ── Descarga PDF ──────────────────────────────────────────────────────────────
// Cada pestaña/página registra una *factory* que, al hacer clic en "Descargar
// Informe", se evalúa para obtener las columnas disponibles (para el modal de
// selección) y la función que genera el PDF con el subconjunto de columnas
// elegido. Devuelve null cuando no hay filas que exportar.
export type PdfExportDescriptor = {
  columnas: string[];                                  // = headCols en ese momento
  generar:  (idxSeleccionados: number[]) => Promise<void>;
};
export type DownloadFactory   = () => PdfExportDescriptor | null;
export type DownloadRegistrar = (fn: DownloadFactory | null) => void;

// Estructura de datos para la tabla del PDF (renderizada con jspdf-autotable,
// sin capturas DOM — garantiza alineación perfecta independiente de CSS).
export interface PdfTableData {
  head:         string[][];
  body:         (string | number | null)[][];
  // Índice de la columna "Cump." que contiene el ratio crudo (0..1), -1 si no existe.
  // didParseCell la formatea como % y aplica colorimetría verde/amarillo/rojo.
  cumpColIndex: number;
  colStyles?:   Record<number, { halign: 'left' | 'right' | 'center' }>;
}

// Filtra una tabla PDF dejando solo las columnas cuyos índices (originales) están
// en `idx`, remapeando cumpColIndex y colStyles a las nuevas posiciones.
export function filtrarColumnas(pt: PdfTableData, idx: number[]): PdfTableData {
  const sel = [...idx].sort((a, b) => a - b);
  const nuevoColStyles: Record<number, { halign: 'left' | 'right' | 'center' }> = {};
  if (pt.colStyles) {
    sel.forEach((oldI, newI) => {
      const estilo = pt.colStyles![oldI];
      if (estilo) nuevoColStyles[newI] = estilo;
    });
  }
  return {
    head:         [sel.map(i => pt.head[0][i])],
    body:         pt.body.map(row => sel.map(i => row[i])),
    cumpColIndex: sel.indexOf(pt.cumpColIndex),
    colStyles:    nuevoColStyles,
  };
}

export async function buildExportarPDF(opts: {
  tituloArchivo: string;
  tituloPDF:     string;
  subtitulo?:    string;
  pdfTable:      PdfTableData;
  refGrafico?:   React.RefObject<HTMLDivElement | null>;
}): Promise<void> {
  const [{ default: jsPDF }, { default: html2canvas }, { autoTable }] = await Promise.all([
    import('jspdf'),
    import('html2canvas'),
    import('jspdf-autotable'),  // v5: exporta autoTable como función standalone
  ]);

  const { tituloArchivo, tituloPDF, subtitulo, pdfTable, refGrafico } = opts;
  const PW = 297; const PH = 210; const M = 10;
  const HEADER_H = 28;
  const FOOTER_H = 10;
  const TOP      = HEADER_H + 5;
  const MAX_CONT = PH - TOP - FOOTER_H;

  const renderHeader = (pdf: InstanceType<typeof jsPDF>, pagina: number) => {
    pdf.setFillColor(37, 99, 235);
    pdf.rect(0, 0, PW, HEADER_H, 'F');
    pdf.setTextColor(255, 255, 255);
    pdf.setFont('helvetica', 'bold');
    pdf.setFontSize(18);
    pdf.text(tituloPDF, M, 13);
    if (subtitulo) {
      pdf.setFont('helvetica', 'normal');
      pdf.setFontSize(9);
      pdf.setTextColor(186, 210, 255);
      pdf.text(subtitulo, M, 21);
    }
    pdf.setDrawColor(203, 213, 225); pdf.setLineWidth(0.3);
    pdf.line(M, PH - FOOTER_H + 2, PW - M, PH - FOOTER_H + 2);
    pdf.setFont('helvetica', 'normal'); pdf.setFontSize(8); pdf.setTextColor(100, 116, 139);
    pdf.text('Zentinela · Sistema de Gestión de Recaudo', M, PH - 4);
    pdf.text(`Página ${pagina}`, PW - M, PH - 4, { align: 'right' });
  };

  const pdf = new jsPDF({ orientation: 'landscape', unit: 'mm', format: 'a4' });

  // ── Página 1: Tabla con jspdf-autotable (sin captura DOM, texto perfectamente centrado) ──
  renderHeader(pdf, 1);
  autoTable(pdf, {
    startY: TOP,
    head:   pdfTable.head,
    body:   pdfTable.body,
    styles: {
      fontSize:    8,
      cellPadding: 2.5,
      valign:      'middle',
      halign:      'right',
      overflow:    'ellipsize',
    },
    headStyles: {
      fillColor: [37, 99, 235],
      textColor: 255,
      fontStyle: 'bold',
      halign:    'center',
      fontSize:  8,
    },
    alternateRowStyles: { fillColor: [248, 250, 252] },
    columnStyles:       pdfTable.colStyles ?? {},
    // Colorimetría de la columna Cump.: ≥100% verde · ≥90% amarillo · <90% rojo
    didParseCell: (data: any) => {
      if (pdfTable.cumpColIndex < 0) return;
      if (data.section !== 'body' || data.column.index !== pdfTable.cumpColIndex) return;
      const val = Number(data.cell.raw);
      if (isNaN(val) || val < 0) {
        data.cell.text              = ['—'];
        data.cell.styles.textColor  = [148, 163, 184];
        data.cell.styles.halign     = 'center';
      } else {
        const pctVal = val * 100;
        data.cell.text = [`${pctVal.toFixed(1)}%`];
        const color: [number, number, number] =
          pctVal >= 100 ? [22, 163, 74] : pctVal >= 90 ? [202, 138, 4] : [220, 38, 38];
        data.cell.styles.textColor = color;
        data.cell.styles.fontStyle = 'bold';
        data.cell.styles.halign    = 'center';
      }
    },
    margin: { left: M, right: M, top: TOP, bottom: FOOTER_H + 4 },
  });

  // ── Página 2: Gráfico con html2canvas (opcional — solo si se pasó refGrafico) ──
  if (refGrafico?.current) {
    pdf.addPage();
    renderHeader(pdf, 2);

    // Resolver CSS vars leyendo estilos computados antes del clone
    const origEls = [refGrafico.current, ...Array.from(refGrafico.current.querySelectorAll<HTMLElement>('*'))];
    const snaps   = origEls.map(el => {
      const cs = getComputedStyle(el);
      return {
        color: cs.color, bg: cs.backgroundColor,
        btc: cs.borderTopColor, brc: cs.borderRightColor,
        bbc: cs.borderBottomColor, blc: cs.borderLeftColor,
      };
    });
    const c = await html2canvas(refGrafico.current, {
      scale: 2, backgroundColor: '#ffffff', logging: false,
      onclone: (_doc, cloned) => {
        const clonedEls = [cloned, ...Array.from(cloned.querySelectorAll<HTMLElement>('*'))];
        clonedEls.forEach((el, i) => {
          const s = snaps[i]; if (!s) return;
          el.style.color             = s.color;
          el.style.backgroundColor   = s.bg;
          el.style.borderTopColor    = s.btc;
          el.style.borderRightColor  = s.brc;
          el.style.borderBottomColor = s.bbc;
          el.style.borderLeftColor   = s.blc;
        });
      },
    });
    const INNER_W = PW - M * 2;
    const ratio   = (c.height / 2) / (c.width / 2);
    const imgW    = INNER_W;
    const imgH    = Math.min(imgW * ratio, MAX_CONT);
    // Centrar vertical y horizontalmente dentro del área disponible
    const x = (PW - imgW) / 2;
    const y = TOP + Math.max(0, (MAX_CONT - imgH) / 2);
    pdf.addImage(c.toDataURL('image/png'), 'PNG', x, y, imgW, imgH);
  }

  pdf.save(`${tituloArchivo}.pdf`);
}

// ── Modal de selección de columnas para el PDF ────────────────────────────────
// Todas las columnas vienen marcadas por defecto. "Ninguna" desmarca todo y
// deshabilita el botón Descargar hasta que se elija al menos una columna.
export function ModalColumnas({ columnas, generando, onConfirm, onClose }: {
  columnas:  string[];
  generando: boolean;
  onConfirm: (idxSeleccionados: number[]) => void;
  onClose:   () => void;
}) {
  const [seleccion, setSeleccion] = useState<number[]>(() => columnas.map((_, i) => i));

  const toggle = (i: number) =>
    setSeleccion(prev => prev.includes(i) ? prev.filter(x => x !== i) : [...prev, i]);

  const btn: React.CSSProperties = {
    padding: '4px 12px', borderRadius: 6, border: '1px solid var(--z-border)',
    background: 'transparent', color: 'var(--z-text-muted)', cursor: 'pointer',
    fontSize: 12, fontWeight: 600,
  };

  return (
    <div
      onClick={onClose}
      style={{
        position: 'fixed', inset: 0, background: 'rgba(15, 23, 42, 0.55)',
        display: 'flex', alignItems: 'center', justifyContent: 'center',
        zIndex: 1000, padding: 16,
      }}
    >
      <div
        onClick={e => e.stopPropagation()}
        style={{
          background: 'var(--z-bg-card)', border: '1px solid var(--z-border)',
          borderRadius: 12, padding: 24, width: 'min(560px, 100%)',
          maxHeight: '85vh', display: 'flex', flexDirection: 'column', gap: 16,
          boxShadow: '0 20px 50px rgba(0,0,0,0.35)',
        }}
      >
        <div style={{ display: 'flex', justifyContent: 'space-between', alignItems: 'flex-start' }}>
          <div>
            <h2 style={{ fontSize: 16, fontWeight: 600, color: 'var(--z-text-primary)', margin: 0 }}>
              Columnas del informe
            </h2>
            <p style={{ fontSize: 12, color: 'var(--z-text-muted)', margin: '4px 0 0' }}>
              Elige las columnas que quieres incluir en el PDF.
            </p>
          </div>
          <button onClick={onClose} style={{ ...btn, border: 'none', fontSize: 18, padding: '0 4px' }}>×</button>
        </div>

        {/* Presets */}
        <div style={{ display: 'flex', gap: 8, flexWrap: 'wrap' }}>
          <button onClick={() => setSeleccion(columnas.map((_, i) => i))} style={btn}>Todas</button>
          <button onClick={() => setSeleccion([])} style={btn}>Ninguna</button>
          <span style={{ fontSize: 12, color: 'var(--z-text-muted)', alignSelf: 'center', marginLeft: 'auto' }}>
            {seleccion.length} de {columnas.length} seleccionadas
          </span>
        </div>

        {/* Grid de columnas */}
        <div style={{
          display: 'grid', gridTemplateColumns: 'repeat(2, 1fr)', gap: '8px 16px',
          overflowY: 'auto', paddingRight: 4,
        }}>
          {columnas.map((col, i) => (
            <label key={i} style={{
              display: 'flex', alignItems: 'center', gap: 8, fontSize: 13,
              color: 'var(--z-text-primary)', cursor: 'pointer',
            }}>
              <input type="checkbox" checked={seleccion.includes(i)} onChange={() => toggle(i)} />
              {col}
            </label>
          ))}
        </div>

        {/* Acciones */}
        <div style={{ display: 'flex', justifyContent: 'flex-end', gap: 10, borderTop: '1px solid var(--z-border)', paddingTop: 16 }}>
          <button onClick={onClose} style={{ ...btn, padding: '8px 16px' }}>Cancelar</button>
          <button
            onClick={() => onConfirm(seleccion)}
            disabled={seleccion.length === 0 || generando}
            style={{
              padding: '8px 18px', borderRadius: 7, fontSize: 13, fontWeight: 600,
              border: 'none', color: '#fff', whiteSpace: 'nowrap',
              background: (seleccion.length === 0 || generando) ? '#94a3b8' : C.naranja,
              cursor: (seleccion.length === 0 || generando) ? 'not-allowed' : 'pointer',
              opacity: (seleccion.length === 0 || generando) ? 0.7 : 1,
            }}
          >
            {generando ? 'Generando…' : 'Descargar'}
          </button>
        </div>
      </div>
    </div>
  );
}

// ── Ordenamiento de tablas ────────────────────────────────────────────────────
export type SortDir = 'asc' | 'desc';

export function useSortable<T extends Record<string, unknown>>(items: T[]) {
  const [key,  setKey]  = useState<keyof T | null>(null);
  const [dir,  setDir]  = useState<SortDir>('asc');

  const handleSort = (k: keyof T) => {
    if (key === k) setDir(d => d === 'asc' ? 'desc' : 'asc');
    else { setKey(k); setDir('asc'); }
  };

  const sorted = useMemo(() => {
    if (!key) return items;
    return [...items].sort((a, b) => {
      const va = a[key], vb = b[key];
      const cmp = typeof va === 'string'
        ? (va as string).localeCompare(vb as string, 'es')
        : (va as number) - (vb as number);
      return dir === 'asc' ? cmp : -cmp;
    });
  }, [items, key, dir]);

  return { sorted, sortKey: key, sortDir: dir, handleSort };
}

// Props de ordenamiento que el padre (dueño del estado de orden) pasa a la tabla hija.
// Mantener el estado en el padre permite que la descarga PDF use el mismo orden visible.
export interface SortHandlers {
  sortKey: string | null;
  sortDir: SortDir;
  onSort:  (k: string) => void;
}

// ── Estilos base para th/td ───────────────────────────────────────────────────
// Encabezado con el mismo estilo que las tablas del Home (ThOrdenable): mayúsculas,
// texto primario; la columna activa se resalta con el color de marca.
export const TH: React.CSSProperties = {
  padding: '10px 14px', fontSize: 11, fontWeight: 600,
  color: 'var(--z-text-primary)', letterSpacing: '0.04em',
  textTransform: 'uppercase', whiteSpace: 'nowrap',
  background: 'var(--z-brand-light)',
  borderBottom: '2px solid var(--z-brand)',
};
export const TD: React.CSSProperties = {
  padding: '9px 12px', borderBottom: '1px solid var(--z-border)', whiteSpace: 'nowrap',
};

export function ThSort({ label, k, sortKey, sortDir, onSort, align = 'right' }: {
  label: string; k: string;
  sortKey: string | null; sortDir: SortDir;
  onSort: (k: string) => void;
  align?: 'left' | 'right' | 'center';
}) {
  const active = sortKey === k;
  return (
    <th
      onClick={() => onSort(k)}
      style={{
        ...TH, textAlign: align, cursor: 'pointer', userSelect: 'none',
        color: active ? 'var(--z-brand)' : 'var(--z-text-primary)',
      }}
    >
      {label}&nbsp;
      <span style={{ fontSize: 10, opacity: active ? 1 : 0.25 }}>
        {active ? (sortDir === 'asc' ? '▲' : '▼') : '▲▼'}
      </span>
    </th>
  );
}

// ── Tooltip y leyenda de gráficos ─────────────────────────────────────────────
export function TooltipCOP({ active, payload, label }: any) {
  if (!active || !payload?.length) return null;
  return (
    <div style={{
      background: 'var(--z-bg-card)', border: '1px solid var(--z-border)',
      borderRadius: 8, padding: '10px 14px', fontSize: 13,
    }}>
      <p style={{ fontWeight: 600, marginBottom: 4, color: 'var(--z-text-primary)' }}>{label}</p>
      {payload.map((p: any) => (
        <p key={p.name} style={{ color: p.color, margin: '2px 0' }}>
          {p.name}: <strong>{cop(p.value)}</strong>
        </p>
      ))}
    </div>
  );
}

export function LeyendaGrafico({ items }: { items: { color: string; label: string }[] }) {
  return (
    <div style={{ display: 'flex', alignItems: 'center', gap: 14, flexWrap: 'wrap' }}>
      {items.map(it => (
        <div key={it.label} style={{ display: 'flex', alignItems: 'center', gap: 5 }}>
          <span style={{ width: 10, height: 10, borderRadius: 2, background: it.color, flexShrink: 0, display: 'inline-block' }} />
          <span style={{ fontSize: 12, color: 'var(--z-text-muted)' }}>{it.label}</span>
        </div>
      ))}
    </div>
  );
}

export function FiltroSelect({ label, value, onChange, options, placeholder }: {
  label: string;
  value: string;
  onChange: (v: string) => void;
  options: { value: string; label: string }[];
  placeholder: string;
}) {
  const sel: React.CSSProperties = {
    background: 'var(--z-bg-card)', border: '1px solid var(--z-border)',
    borderRadius: 6, padding: '5px 10px', fontSize: 13,
    color: 'var(--z-text-primary)', cursor: 'pointer', minWidth: 160,
  };
  return (
    <div style={{ display: 'flex', alignItems: 'center', gap: 6 }}>
      <span style={{ fontSize: 12, color: 'var(--z-text-muted)', whiteSpace: 'nowrap' }}>{label}</span>
      <select value={value} onChange={e => onChange(e.target.value)} style={sel}>
        <option value="">{placeholder}</option>
        {options.map(o => <option key={o.value} value={o.value}>{o.label}</option>)}
      </select>
    </div>
  );
}
