import XLSX from 'xlsx-js-style';
import api from './authApi';
import type { AnalizarAuditoriaResponse, InformeAuditoria, EstimacionCosto, ReporteGenerado } from '../types/auditoriaGestiones';
import type { CarteraLiderDropdownDto } from '../types/calificador';

// El selector de carteras puede probarse contra la BD real (flag de respaldo por si se quiere mock).
const USAR_MOCK_OPCIONES = false;

const SEV_TEXTO: Record<string, string> = { critico: 'Crítico', medio: 'Medio', bajo: 'Bajo', revision: 'Requiere revisión' };
// Etiqueta legible del tipo de hallazgo.
const TIPO_TEXTO: Record<string, string> = {
  'perfil-gestion':    'Perfil ↔ Gestión',
  'promesa-sin-valor': 'Promesa sin valor',
  'valor-sin-fecha':   'Valor sin fecha',
  'valor-bajo':        'Valor < 5 dígitos',
};
// Severidad en tono PASTEL: relleno claro + texto oscuro del mismo matiz.
const SEV_FILL: Record<string, string> = { critico: 'FADBD8', medio: 'FDEBD0', bajo: 'D6EEF5', revision: 'E0E7FF' };
const SEV_TXT:  Record<string, string> = { critico: '991B1B', medio: '92400E', bajo: '0E7490', revision: '3730A3' };

// Identidad de la página: naranja de marca + filas zebra cálidas; tipografía Segoe UI.
const MARCA = 'F54927', TITULO = '7C2D12', ZEBRA = 'FBF3F0', BLANCO = 'FFFFFF';
const FUENTE = 'Segoe UI';
const BORDE = { style: 'thin', color: { rgb: 'F0D9D0' } };
const TODOS_BORDES = { top: BORDE, bottom: BORDE, left: BORDE, right: BORDE };
const CABECERA = { fill: { fgColor: { rgb: MARCA } }, font: { name: FUENTE, bold: true, color: { rgb: BLANCO }, sz: 11 }, alignment: { vertical: 'center' }, border: TODOS_BORDES };

// Primera letra en mayúscula, el resto en minúscula.
const capitalizar = (s: string) => (s ? s.charAt(0).toUpperCase() + s.slice(1).toLowerCase() : s);
// Primera letra de cada palabra en mayúscula (ej. "DANIELA AGUIRRE" → "Daniela Aguirre").
const tituloCase = (s: string) => (s ? s.toLowerCase().split(' ').map(w => (w ? w[0].toUpperCase() + w.slice(1) : w)).join(' ') : s);
// Fecha ISO (yyyy-mm-dd) → dd/mm/yyyy.
const fechaLegible = (iso: string) => (iso ? iso.split('-').reverse().join('/') : '');

// Pone estilo a una celda (si existe) por fila/columna.
const estilo = (ws: XLSX.WorkSheet, r: number, c: number, s: object) => {
  const celda = ws[XLSX.utils.encode_cell({ r, c })] as { s?: object } | undefined;
  if (celda) celda.s = s;
};

// Estiliza una tabla: cabecera naranja + filas zebra con bordes. col0 = columna inicial.
const estilizarTabla = (ws: XLSX.WorkSheet, headerRow: number, nDatos: number, nCols: number, col0 = 0) => {
  for (let c = 0; c < nCols; c++) estilo(ws, headerRow, col0 + c, CABECERA);
  for (let i = 0; i < nDatos; i++) {
    const base = i % 2 === 1 ? { fill: { fgColor: { rgb: ZEBRA } } } : {};
    for (let c = 0; c < nCols; c++) estilo(ws, headerRow + 1 + i, col0 + c, { ...base, font: { name: FUENTE, sz: 10 }, border: TODOS_BORDES, alignment: { vertical: 'center', wrapText: true } });
  }
};

/// Descarga el informe en .xlsx con estilo de informe: hoja "Resumen" + una hoja por líder.
export const descargarAuditoriaCompleta = (informe: InformeAuditoria, dia?: string): void => {
  const wb = XLSX.utils.book_new();
  const cartera = informe.incongruenciasPorCartera[0]?.cartera ?? '';
  const totalHallazgos = informe.totalCriticas + informe.totalMedias + informe.totalBajas + informe.totalRevision;
  const subtitulo = [cartera && `Cartera: ${cartera}`, dia && `Día: ${fechaLegible(dia)}`].filter(Boolean).join('   ·   ')
    || 'Informe de auditoría de gestiones';

  // ── Hoja 1: Resumen (tablero de dos columnas) ─────────────────────────────
  // Columnas: A margen exterior · B marco izq · C aire · D-F tablas izquierda ·
  //           G-H separación · I-K tablas derecha · L aire · M marco der.
  const SUAVE = 'FEF0EC';                                 // naranja muy claro (brandLight)
  const ACENTO = { left: { style: 'medium', color: { rgb: MARCA } }, top: BORDE, bottom: BORDE, right: BORDE };
  const FRAME_L = 1, FRAME_R = 12;                        // columnas del marco (con 1 celda de aire hacia el contenido)
  const LEFT = 3, RIGHT = 8, ULTIMA_COL = FRAME_R;        // primeras columnas de cada bloque de tablas (G-H separan)
  const filas: (string | number)[][] = [];
  const merges: { s: { r: number; c: number }; e: { r: number; c: number } }[] = [];
  // Coloca un valor en una celda concreta (r, c), creciendo la matriz si hace falta.
  const set = (r: number, c: number, v: string | number) => { while (filas.length <= r) filas.push([]); filas[r][c] = v; };

  // Título + subtítulo (filas 1 y 2; la fila 0 queda como aire bajo el marco superior)
  set(1, LEFT, 'AUDITORÍA DE GESTIONES');
  set(2, LEFT, subtitulo);

  // KPIs centrados (filas 4-6): etiqueta combinada + valor a la derecha
  const kpis: [string, string | number][] = [
    ['% de inconsistencias', `${informe.porcentajeIncongruencias.toFixed(1)}%`],
    ['Total de gestiones', informe.totalGestiones],
    ['Hallazgos', totalHallazgos],
  ];
  const kpiRows = kpis.map((_, i) => 4 + i);
  kpis.forEach(([lbl, val], i) => { set(4 + i, LEFT + 1, lbl); set(4 + i, LEFT + 4, val); });

  const secciones: { row: number; c0: number; c1: number }[] = [];
  const tablas: { row: number; nDatos: number; nCols: number; col0: number }[] = [];

  // ── Columna izquierda: Severidad + Tipificaciones ──
  // (arranca en la fila 9: filas 7-8 quedan en blanco tras el bloque de porcentajes/KPIs)
  let lr = 9;
  secciones.push({ row: lr, c0: LEFT, c1: LEFT + 1 }); set(lr, LEFT, 'HALLAZGOS POR SEVERIDAD'); lr++;
  const sevH = lr; set(lr, LEFT, 'Severidad'); set(lr, LEFT + 1, 'Cantidad'); lr++;
  const sevRows = [
    { sev: 'critico', n: informe.totalCriticas }, { sev: 'medio', n: informe.totalMedias },
    { sev: 'bajo', n: informe.totalBajas }, { sev: 'revision', n: informe.totalRevision },
  ];
  for (const s of sevRows) { set(lr, LEFT, SEV_TEXTO[s.sev]); set(lr, LEFT + 1, s.n); lr++; }
  tablas.push({ row: sevH, nDatos: sevRows.length, nCols: 2, col0: LEFT });
  lr++;                                                        // fila en blanco

  secciones.push({ row: lr, c0: LEFT, c1: LEFT + 2 }); set(lr, LEFT, 'TIPIFICACIONES CON MÁS INCONGRUENCIAS'); lr++;
  const tipH = lr; set(lr, LEFT, 'Tipificación'); set(lr, LEFT + 1, 'Incongruencias'); set(lr, LEFT + 2, 'Total'); lr++;
  for (const t of informe.tipificacionesConMasIncongruencias) { set(lr, LEFT, capitalizar(t.tipificacion)); set(lr, LEFT + 1, t.incongruencias); set(lr, LEFT + 2, t.total); lr++; }
  tablas.push({ row: tipH, nDatos: informe.tipificacionesConMasIncongruencias.length, nCols: 3, col0: LEFT });

  // ── Columna derecha: Palabra más repetida + inconsistentes + Top palabras ──
  let rr = 9;
  const bloquePalabra = (titulo: string, palabra: string, conteo: number) => {
    secciones.push({ row: rr, c0: RIGHT, c1: RIGHT + 2 }); set(rr, RIGHT, titulo); rr++;
    const h = rr; set(rr, RIGHT, 'Palabra'); set(rr, RIGHT + 1, 'Apariciones'); rr++;
    set(rr, RIGHT, palabra || '—'); set(rr, RIGHT + 1, conteo); rr++;
    tablas.push({ row: h, nDatos: 1, nCols: 2, col0: RIGHT });
    rr++;                                                      // fila en blanco
  };
  bloquePalabra('PALABRA MÁS REPETIDA', informe.palabraMasRepetida.palabra, informe.palabraMasRepetida.conteo);
  bloquePalabra('PALABRA EN GESTIONES INCONSISTENTES', informe.palabraInconsistencias.palabra, informe.palabraInconsistencias.conteo);

  secciones.push({ row: rr, c0: RIGHT, c1: RIGHT + 2 }); set(rr, RIGHT, 'TOP PALABRAS EN GESTIONES'); rr++;
  const topH = rr; set(rr, RIGHT, 'Palabra'); set(rr, RIGHT + 1, 'Apariciones'); rr++;
  for (const p of informe.topPalabras) { set(rr, RIGHT, p.palabra); set(rr, RIGHT + 1, p.conteo); rr++; }
  tablas.push({ row: topH, nDatos: informe.topPalabras.length, nCols: 2, col0: RIGHT });

  filas.push([]);                                              // fila de aire sobre el marco inferior

  const wsResumen = XLSX.utils.aoa_to_sheet(filas);

  // Aplica un estilo a un rango de celdas de la misma fila y registra el merge.
  const banda = (r: number, c0: number, c1: number, s: object) => {
    for (let c = c0; c <= c1; c++) estilo(wsResumen, r, c, s);
    merges.push({ s: { r, c: c0 }, e: { r, c: c1 } });
  };

  // Rellenar de blanco el área del reporte (oculta la cuadrícula dentro de esa zona; el
  // resto de estilos se aplica encima). La librería no puede apagar la cuadrícula global.
  const ultimaFila = filas.length - 1;
  const ultimaCol  = ULTIMA_COL;
  for (let r = 0; r <= ultimaFila; r++) for (let c = 0; c <= ultimaCol; c++) {
    const ref = XLSX.utils.encode_cell({ r, c });
    if (!wsResumen[ref]) wsResumen[ref] = { t: 's', v: '' };
    (wsResumen[ref] as { s?: object }).s = { fill: { fgColor: { rgb: 'FFFFFF' } } };
  }
  // Forzar el rango desde A1: aoa_to_sheet lo origina en la primera celda con dato (p.ej. D2),
  // y las celdas del marco/margen (col A-C, fila 0) quedarían fuera del rango y NO se escribirían.
  wsResumen['!ref'] = XLSX.utils.encode_range({ s: { r: 0, c: 0 }, e: { r: ultimaFila, c: ultimaCol } });

  // Banner del título + subtítulo (fondo naranja claro, línea naranja debajo), centrados
  banda(1, LEFT, RIGHT + 2, { fill: { fgColor: { rgb: SUAVE } }, font: { name: FUENTE, bold: true, sz: 18, color: { rgb: TITULO } }, alignment: { horizontal: 'center', vertical: 'center' }, border: { bottom: { style: 'medium', color: { rgb: MARCA } } } });
  banda(2, LEFT, RIGHT + 2, { font: { name: FUENTE, italic: true, sz: 10, color: { rgb: MARCA } }, alignment: { horizontal: 'center', vertical: 'center' } });

  // KPIs (información general) centrados y con MARCO naranja: etiqueta combinada + valor.
  const MED = { style: 'medium', color: { rgb: MARCA } };
  const MARCO = { top: MED, bottom: MED, left: MED, right: MED };
  for (const r of kpiRows) {
    banda(r, LEFT + 1, LEFT + 3, { fill: { fgColor: { rgb: SUAVE } }, font: { name: FUENTE, bold: true, sz: 11, color: { rgb: TITULO } }, alignment: { horizontal: 'center', vertical: 'center' }, border: MARCO });
    // El valor se combina en dos columnas (H:I) para que no se corte (H es angosta por ser separación).
    banda(r, LEFT + 4, LEFT + 5, { fill: { fgColor: { rgb: SUAVE } }, font: { name: FUENTE, bold: true, sz: 16, color: { rgb: MARCA } }, alignment: { horizontal: 'center', vertical: 'center' }, border: MARCO });
  }

  // Barras de sección (fondo suave + acento naranja a la izquierda)
  for (const s of secciones) banda(s.row, s.c0, s.c1, { fill: { fgColor: { rgb: SUAVE } }, font: { name: FUENTE, bold: true, sz: 12, color: { rgb: TITULO } }, alignment: { vertical: 'center' }, border: ACENTO });

  for (const t of tablas) estilizarTabla(wsResumen, t.row, t.nDatos, t.nCols, t.col0);
  // Badge de severidad pastel en la primera columna de su tabla
  sevRows.forEach((s, i) => estilo(wsResumen, sevH + 1 + i, LEFT, { fill: { fgColor: { rgb: SEV_FILL[s.sev] } }, font: { name: FUENTE, bold: true, color: { rgb: SEV_TXT[s.sev] } }, border: TODOS_BORDES }));

  // Marco naranja oscuro: rectángulo limpio de (0, FRAME_L) a (ultimaFila, FRAME_R); deja
  // 1 celda de aire hacia el contenido y la columna A como margen exterior (borde visible).
  const ORO = 'C2410C';                                      // naranja oscuro
  const LADO = { style: 'medium', color: { rgb: ORO } };
  const addBorde = (r: number, c: number, lados: object) => {
    const celda = wsResumen[XLSX.utils.encode_cell({ r, c })] as { s?: { border?: object } } | undefined;
    if (!celda) return;
    const s = (celda.s ?? {}) as { border?: object };
    s.border = { ...(s.border ?? {}), ...lados };
    celda.s = s;
  };
  for (let c = FRAME_L; c <= FRAME_R; c++) { addBorde(0, c, { top: LADO }); addBorde(ultimaFila, c, { bottom: LADO }); }
  for (let r = 0; r <= ultimaFila; r++) { addBorde(r, FRAME_L, { left: LADO }); addBorde(r, FRAME_R, { right: LADO }); }

  wsResumen['!cols'] = [
    { wch: 2 }, { wch: 2 }, { wch: 3 },          // A margen · B marco · C aire
    { wch: 30 }, { wch: 16 }, { wch: 10 },        // D-F izquierda
    { wch: 4 }, { wch: 4 },                       // G-H separación (dos celdas)
    { wch: 26 }, { wch: 13 }, { wch: 5 },         // I-K derecha
    { wch: 3 }, { wch: 2 },                       // L aire · M marco
  ];
  const rowsResumen: { hpt: number }[] = [];
  rowsResumen[0] = { hpt: 8 }; rowsResumen[1] = { hpt: 30 }; rowsResumen[2] = { hpt: 18 };
  for (const r of kpiRows) rowsResumen[r] = { hpt: 26 };
  wsResumen['!rows'] = rowsResumen;
  wsResumen['!merges'] = merges;
  XLSX.utils.book_append_sheet(wb, wsResumen, 'Resumen');

  // ── Hoja "Detalle": TODOS los hallazgos, ordenados por severidad ──────────────
  const encabezados = ['Tipo de alerta', 'Severidad', 'Detalle', 'Líder', 'Asesor', 'Titular', 'Último Perfil', 'Gestión', 'Fecha Promesa', 'Valor Promesa'];
  const legible = (g: string) => g.replace(/,/g, ' ').replace(/\s+/g, ' ').trim();
  const fechaDMY = (iso: string | null) => (iso ? iso.split('-').reverse().join('/') : '');
  // Orden solicitado: requiere revisión → crítico → medio → bajo.
  const ORDEN_SEV: Record<string, number> = { revision: 0, critico: 1, medio: 2, bajo: 3 };
  const items = informe.incongruenciasPorLider
    .flatMap(g => g.items.map(it => ({ it, lider: g.lider })))
    .sort((a, b) => (ORDEN_SEV[a.it.severidad] ?? 9) - (ORDEN_SEV[b.it.severidad] ?? 9));
  const filasD: (string | number)[][] = [
    [`Detalle de hallazgos — ${subtitulo}`],   // fila de título (arriba de los encabezados)
    encabezados,
    ...items.map(({ it, lider }) => [
      TIPO_TEXTO[it.tipo] ?? it.tipo, SEV_TEXTO[it.severidad] ?? it.severidad, it.detalle,
      tituloCase(lider), tituloCase(it.asesorGestion), it.identificacion,
      capitalizar(it.ultimoPerfil), legible(it.gestion), fechaDMY(it.fechaPromesa), it.valorPromesa ?? '',
    ]),
  ];
  const wsDetalle = XLSX.utils.aoa_to_sheet(filasD);

  // Fila de título (banner naranja) sobre la fila 0, encabezados en la fila 1.
  for (let c = 0; c < 10; c++) estilo(wsDetalle, 0, c, { fill: { fgColor: { rgb: MARCA } }, font: { name: FUENTE, bold: true, sz: 13, color: { rgb: BLANCO } }, alignment: { horizontal: 'left', vertical: 'center' } });
  estilizarTabla(wsDetalle, 1, items.length, 10);
  // Tipo (0) centrado; Severidad (1) pastel; Detalle (2) y Gestión (6) ajustan texto; Último Perfil (7), Fecha (8), Valor (9) centrados.
  items.forEach(({ it }, i) => {
    const r = i + 2;
    const zebra = i % 2 === 1 ? { fill: { fgColor: { rgb: ZEBRA } } } : {};
    const wrap    = { ...zebra, font: { name: FUENTE, sz: 10 }, border: TODOS_BORDES, alignment: { vertical: 'top', wrapText: true } };
    const wrapMed = { ...zebra, font: { name: FUENTE, sz: 10 }, border: TODOS_BORDES, alignment: { vertical: 'center', wrapText: true } };
    const centro  = { ...zebra, font: { name: FUENTE, sz: 10 }, border: TODOS_BORDES, alignment: { horizontal: 'center', vertical: 'center', wrapText: true } };
    estilo(wsDetalle, r, 0, centro);  // Tipo de alerta
    estilo(wsDetalle, r, 1, { ...zebra, fill: { fgColor: { rgb: SEV_FILL[it.severidad] ?? 'E5E7EB' } }, font: { name: FUENTE, bold: true, color: { rgb: SEV_TXT[it.severidad] ?? '374151' } }, alignment: { horizontal: 'center', vertical: 'center', wrapText: true }, border: TODOS_BORDES });  // Severidad
    estilo(wsDetalle, r, 2, wrapMed); // Detalle (centrado vertical, texto a la izquierda)
    estilo(wsDetalle, r, 3, centro);  // Líder
    estilo(wsDetalle, r, 4, centro);  // Asesor
    estilo(wsDetalle, r, 5, centro);  // Titular
    estilo(wsDetalle, r, 6, centro);  // Último Perfil
    estilo(wsDetalle, r, 7, wrap);    // Gestión
    estilo(wsDetalle, r, 8, centro);  // Fecha Promesa
    estilo(wsDetalle, r, 9, centro);  // Valor Promesa
  });

  wsDetalle['!cols'] = [{ wch: 18 }, { wch: 16 }, { wch: 40 }, { wch: 22 }, { wch: 22 }, { wch: 14 }, { wch: 18 }, { wch: 44 }, { wch: 14 }, { wch: 14 }];
  wsDetalle['!merges'] = [{ s: { r: 0, c: 0 }, e: { r: 0, c: 9 } }];              // título combinado A1:J1
  wsDetalle['!rows'] = [{ hpt: 26 }];                                             // alto de la fila de título
  // AutoFiltro sobre la fila de encabezados (fila 2) y todos los datos → el filtro sigue funcionando.
  wsDetalle['!autofilter'] = { ref: XLSX.utils.encode_range({ s: { r: 1, c: 0 }, e: { r: 1 + items.length, c: 9 } }) };
  XLSX.utils.book_append_sheet(wb, wsDetalle, 'Detalle');

  XLSX.writeFile(wb, `auditoria_gestiones${dia ? '_' + dia : ''}.xlsx`);
};

export interface CarteraSeleccion {
  carteraID: number;
  liderID: number | null;
}

/// Audita/compara una o varias carteras consultando HistoricoEstadoTitular,
/// filtrando las gestiones cuyo FechaUltimaGestion cae entre fechaInicio y fechaFin.
export const analizarAuditoria = async (
  carteras: CarteraSeleccion[], fechaInicio: string, fechaFin: string,
): Promise<AnalizarAuditoriaResponse> => {
  const { data } = await api.post<AnalizarAuditoriaResponse>(
    '/api/informes/auditoria-gestiones/analizar',
    { carteras, fechaInicio, fechaFin },
  );
  return data;
};

/// Lista los reportes ya generados y disponibles en memoria del servidor (compartidos, 48 h).
export const obtenerReportesGenerados = async (): Promise<ReporteGenerado[]> => {
  const { data } = await api.get<ReporteGenerado[]>('/api/informes/auditoria-gestiones/reportes');
  return data;
};

/// Estima el costo aproximado (COP) de evaluar con IA las gestiones del filtro.
export const estimarCostoAuditoria = async (
  carteras: CarteraSeleccion[], fechaInicio: string, fechaFin: string,
): Promise<EstimacionCosto> => {
  const { data } = await api.post<EstimacionCosto>(
    '/api/informes/auditoria-gestiones/estimar-costo',
    { carteras, fechaInicio, fechaFin },
  );
  return data;
};

// Opciones cartera–líder para el selector previo a la carga.
const OPCIONES_MOCK: CarteraLiderDropdownDto[] = [
  { carteraID: 13, liderID: 1, etiqueta: 'PELICANO PROPIA — Elena Salcedo' },
  { carteraID: 20, liderID: 2, etiqueta: 'LUMEN — Mario Quiroga' },
  { carteraID: 21, liderID: 3, etiqueta: 'MERCADEOS PERSONAL ALMACEN — Carlos Rodríguez' },
  { carteraID: 36, liderID: 4, etiqueta: 'NUEVA EPS — María Gómez' },
];

export const obtenerOpcionesAuditoria = async (): Promise<CarteraLiderDropdownDto[]> => {
  if (USAR_MOCK_OPCIONES) {
    await new Promise(r => setTimeout(r, 200));
    return OPCIONES_MOCK;
  }
  const { data } = await api.get<CarteraLiderDropdownDto[]>('/api/calificador/opciones');
  return data;
};
