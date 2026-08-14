// Exportación a PDF del análisis IVR, con las mismas secciones que muestra el panel:
// resumen global, apreciación, embudo, desenlaces, comparativo por líder y, por cada
// líder, su embudo, sus desenlaces y el detalle por cartera.
//
// No se reutiliza `buildExportarPDF` de reporteCompartido: ese está hecho para UNA
// tabla (+ un gráfico opcional) y este informe son varias secciones encadenadas con
// paginación propia. Sí se comparten las librerías y el lenguaje visual (banda de
// encabezado, pie con paginación).
import type { AnalisisIvr, AnalisisIvrLider, DesenlaceIvr, EmbudoPasoIvr } from '../../../types/virtualidad';

const n   = (v: number) => v.toLocaleString('es-CO');
const pct = (v: number) => `${v.toFixed(2)}%`;

// A4 vertical: el comparativo tiene 7 columnas y entra cómodo; las secciones por
// líder son sobre todo texto, que se lee mejor en vertical.
const PW = 210, PH = 297, M = 12;
const HEADER_H = 26, FOOTER_H = 12;
const TOP = HEADER_H + 6;
const LIMITE_Y = PH - FOOTER_H - 4;

const AZUL: [number, number, number] = [8, 145, 178];   // C.ivr del panel
const GRIS: [number, number, number] = [100, 116, 139];

export async function exportarAnalisisIvrPdf(a: AnalisisIvr): Promise<void> {
  const [{ default: jsPDF }, { autoTable }] = await Promise.all([
    import('jspdf'),
    import('jspdf-autotable'),
  ]);

  const pdf = new jsPDF({ orientation: 'portrait', unit: 'mm', format: 'a4' });
  let pagina = 1;

  const periodo = `${fecha(a.desde)} al ${fecha(a.hasta)}`;

  const encabezado = () => {
    pdf.setFillColor(...AZUL);
    pdf.rect(0, 0, PW, HEADER_H, 'F');
    pdf.setTextColor(255, 255, 255);
    pdf.setFont('helvetica', 'bold');
    pdf.setFontSize(16);
    pdf.text('ANÁLISIS IVR', M, 12);
    pdf.setFont('helvetica', 'normal');
    pdf.setFontSize(9);
    pdf.setTextColor(190, 230, 240);
    pdf.text(periodo, M, 19);

    pdf.setDrawColor(203, 213, 225); pdf.setLineWidth(0.3);
    pdf.line(M, PH - FOOTER_H + 2, PW - M, PH - FOOTER_H + 2);
    pdf.setFont('helvetica', 'normal'); pdf.setFontSize(8); pdf.setTextColor(...GRIS);
    pdf.text('Zentinela · Análisis de IVR', M, PH - 5);
    pdf.text(`Página ${pagina}`, PW - M, PH - 5, { align: 'right' });
  };

  /** Salta de página si lo que viene no entra en lo que queda. */
  const asegurar = (alto: number, y: number): number => {
    if (y + alto <= LIMITE_Y) return y;
    pdf.addPage();
    pagina++;
    encabezado();
    return TOP;
  };

  const titulo = (texto: string, y: number): number => {
    const yy = asegurar(12, y);
    pdf.setFont('helvetica', 'bold'); pdf.setFontSize(11);
    pdf.setTextColor(30, 41, 59);
    pdf.text(texto, M, yy);
    return yy + 5;
  };

  const parrafo = (texto: string, y: number, ancho = PW - M * 2): number => {
    pdf.setFont('helvetica', 'normal'); pdf.setFontSize(9);
    pdf.setTextColor(51, 65, 85);
    const lineas = pdf.splitTextToSize(texto, ancho) as string[];
    const yy = asegurar(lineas.length * 4.2 + 3, y);
    pdf.text(lineas, M, yy);
    return yy + lineas.length * 4.2 + 3;
  };

  const tabla = (head: string[][], body: (string | number)[][], y: number, anchos?: Record<number, number>): number => {
    autoTable(pdf, {
      startY: y,
      head, body,
      styles:      { fontSize: 8, cellPadding: 1.8, valign: 'middle', overflow: 'linebreak' },
      headStyles:  { fillColor: AZUL, textColor: 255, fontStyle: 'bold', fontSize: 8 },
      alternateRowStyles: { fillColor: [248, 250, 252] },
      columnStyles: anchos
        ? Object.fromEntries(Object.entries(anchos).map(([k, v]) => [k, { cellWidth: v }]))
        : {},
      margin: { left: M, right: M, top: TOP, bottom: FOOTER_H + 4 },
      didDrawPage: () => {
        // autoTable pagina solo; hay que repintar la banda en cada página nueva.
        if (pdf.getNumberOfPages() > pagina) { pagina = pdf.getNumberOfPages(); encabezado(); }
      },
    });
    return (pdf as unknown as { lastAutoTable: { finalY: number } }).lastAutoTable.finalY + 6;
  };

  const filasEmbudo = (pasos: EmbudoPasoIvr[]) =>
    pasos.map(p => [
      p.etiqueta,
      barra(p.porcentaje),
      n(p.clientes),
      `${pct(p.porcentaje)}${p.base === 'de contactó' ? ' de contactó' : ''}`,
    ]);

  const filasDesenlaces = (ds: DesenlaceIvr[]) =>
    ds.map(d => [d.desenlace, n(d.clientes), pct(d.porcentaje)]);

  // ── Documento ─────────────────────────────────────────────────────────────
  encabezado();
  let y = TOP;

  // Resumen global
  y = titulo('RESUMEN GLOBAL', y);
  y = tabla(
    [['Clientes con resultado', 'Contactados', 'Escuchó el menú', 'Marcaron opción 1']],
    [[n(a.clientesConResultado), n(a.contactados), n(a.escucharonMenu), n(a.marcaronOpcion1)]],
    y,
  );
  y = tabla(
    [['Contactabilidad', 'Conversión a opción 1', 'Marcaciones totales', 'Líderes / carteras']],
    [[pct(a.contactabilidad), pct(a.conversionOpcion1), n(a.totalMarcaciones), `${a.totalLideres} / ${a.totalCarteras}`]],
    y,
  );

  // Aviso de datos incompletos — el mismo que muestra el panel.
  if (a.sinResultado > 0) {
    y = parrafo(
      `AVISO: ${n(a.sinResultado)} de ${n(a.clientesMarcados)} clientes no tienen resultado ` +
      `cargado y quedan fuera del cálculo. Vienen del flujo anterior de dos fases, que ` +
      `registraba el envío pero nunca recibió su resultado. Los porcentajes se calculan ` +
      `sobre los ${n(a.clientesConResultado)} que sí lo tienen.`,
      y,
    );
  }

  if (a.apreciacion) y = parrafo(a.apreciacion, y);

  // Embudo global
  y = titulo('PORCENTAJE GENERAL', y);
  y = tabla([['Etapa', '', 'Clientes', '%']], filasEmbudo(a.embudo), y, { 1: 46 });

  // Desenlaces globales
  y = titulo('DESENLACE DE LOS CONTACTADOS', y);
  y = tabla([['Desenlace', 'Clientes', '% contact.']], filasDesenlaces(a.desenlaces), y);

  // Comparativo por líder
  y = titulo('COMPARATIVO POR LÍDER', y);
  y = tabla(
    [['Líder', 'Con result.', 'Contact.', '% Cont.', 'Escuchó menú', 'Marc. 1', '% Conv.']],
    [
      ...a.porLider.map(l => [
        l.nombreLider, n(l.clientesConResultado), n(l.contactados), pct(l.contactabilidad),
        n(l.escucharonMenu), n(l.marcaronOpcion1), pct(l.conversionOpcion1),
      ]),
      [
        'TOTAL', n(a.clientesConResultado), n(a.contactados), pct(a.contactabilidad),
        n(a.escucharonMenu), n(a.marcaronOpcion1), pct(a.conversionOpcion1),
      ],
    ],
    y,
  );

  // Detalle por líder
  for (const l of a.porLider) {
    y = detalleLider(l, y);
  }

  // Nota metodológica
  y = titulo('NOTA METODOLÓGICA', y);
  y = parrafo(
    'Todos los porcentajes se calculan sobre clientes únicos por cartera. Un cliente marcado ' +
    'varias veces cuenta una sola vez, con su mejor desenlace según la jerarquía: marcó 1 > ' +
    'escuchó sin marcar > sin marcación válida > solo audio > colgó.',
    y,
  );
  y = parrafo(
    'Sobre "sin marcación válida": el IVR solo registra la opción cuando es válida y dispara la ' +
    'transferencia. Una tecla equivocada no queda registrada y es indistinguible de no marcar ' +
    'nada, por lo que esta categoría agrupa ambos casos.',
    y,
  );
  y = parrafo(
    'Quedan fuera de "contactados" los desenlaces sin contacto real: congestión de red, errores ' +
    'de proceso o de sistema, y pasos internos del marcador. No incluye "Solicitadas" ni ' +
    '"Enviadas", que no existen en el modelo de carga única, ni "Atendidos" / "% Atención", que ' +
    'provienen del CDR.',
    y,
  );

  pdf.save(`analisis_ivr_${a.desde}_${a.hasta}.pdf`);

  // ── Sección por líder ─────────────────────────────────────────────────────
  function detalleLider(l: AnalisisIvrLider, yIn: number): number {
    // Se reserva espacio para que el nombre no quede huérfano al pie de página.
    let yy = asegurar(30, yIn);
    pdf.setFont('helvetica', 'bold'); pdf.setFontSize(12);
    pdf.setTextColor(...AZUL);
    pdf.text(l.nombreLider.toUpperCase(), M, yy);
    yy += 4.5;

    pdf.setFont('helvetica', 'normal'); pdf.setFontSize(8);
    pdf.setTextColor(...GRIS);
    pdf.text(`${l.totalCarteras} cartera(s) en el período`, M, yy);
    yy += 5;

    yy = tabla([['Etapa', '', 'Clientes', '%']], filasEmbudo(l.embudo), yy, { 1: 46 });
    yy = tabla([['Desenlace', 'Clientes', '% contact.']], filasDesenlaces(l.desenlaces), yy);
    yy = tabla(
      [['Cartera', 'Con result.', 'Contact.', '% Cont.', 'Marc. 1']],
      l.porCartera.map(c => [
        c.nombreCartera, n(c.clientesConResultado), n(c.contactados),
        pct(c.contactabilidad), n(c.marcaronOpcion1),
      ]),
      yy,
    );

    if (l.apreciacion) yy = parrafo(l.apreciacion, yy);
    return yy + 2;
  }
}

/** Barra de bloques equivalente a la del panel, para que el PDF se lea igual. */
function barra(porcentaje: number): string {
  const BLOQUES = 20;
  const llenos = Math.max(1, Math.min(BLOQUES, Math.round((porcentaje / 100) * BLOQUES)));
  return '#'.repeat(llenos) + '.'.repeat(BLOQUES - llenos);
}

function fecha(iso: string): string {
  if (!iso) return '';
  const [y, m, d] = iso.split('-');
  return `${d}/${m}/${y}`;
}
