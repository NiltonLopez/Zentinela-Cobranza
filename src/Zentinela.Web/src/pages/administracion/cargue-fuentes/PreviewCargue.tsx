// Vista previa de un cargue validado (estado PreviewListo): chips de conteo desplegables
// (cada uno muestra de qué se compone, con filas de ejemplo, sobre TODO el archivo), muestra
// de filas ya limpias y los botones para confirmar o descartar. Nada se ha escrito todavía.
//
// Las excluidas y las advertencias ya no van en listas aparte: son los chips "excluidas" y
// "advertencias", con los mismos grupos más ejemplos, y arrancan abiertos (como se veían
// antes las listas). "Información" sigue como lista: no es el desglose de ningún número.
import type { CargueJob, PreviewCargue as Preview } from '../../../types/cargueFuentes';
import { Aviso } from '../../../components/Aviso';
import { TH, TD, ListaAgrupada, btnPrimario, btnSecundario, cajaError } from '../historico-virtualidad/compartidos';
import { DesgloseChips, type ChipDef } from './DesgloseChips';
import { PALETAS, fechaLegible, num } from './formato';

function chipsDelPreview(p: Preview, totalAdvertencias: number): ChipDef[] {
  const nombres = p.nombresReparados + p.nombresIrrecuperables;
  const chips: (ChipDef | false)[] = [
    { clave: 'Leidas',            numero: p.totalLeidas,         label: 'filas leídas',         paleta: PALETAS.gris },
    { clave: 'Excluidas',         numero: p.excluidas,           label: 'excluidas',            paleta: p.excluidas > 0 ? PALETAS.rojo : PALETAS.gris },
    p.duplicadasEnArchivo > 0 &&
    { clave: 'Duplicadas',        numero: p.duplicadasEnArchivo, label: 'repetidas en el archivo', paleta: PALETAS.gris },
    { clave: 'Insertadas',        numero: p.insertadas,          label: 'se insertarán',        paleta: PALETAS.verde },
    { clave: 'Refrescadas',       numero: p.refrescadas,         label: 'se refrescarán',       paleta: PALETAS.azul },
    { clave: 'VaciasDescartadas', numero: p.descartadasVacias,   label: 'vacías descartadas',   paleta: PALETAS.gris },
    p.vaciasQuePurgara > 0 &&
    { clave: 'VaciasPurgadas',    numero: p.vaciasQuePurgara,    label: 'vacías que se purgarán', paleta: PALETAS.gris },
    { clave: 'TitularesNuevos',   numero: p.titularesNuevos,     label: 'titulares nuevos',     paleta: PALETAS.azul, unidad: ['titular', 'titulares'] },
    { clave: 'NombresReparados',  numero: nombres,
      label: p.nombresIrrecuperables > 0 ? 'nombres reparados / irrecuperables' : 'nombres reparados', paleta: PALETAS.azul, unidad: null },
    { clave: 'RelacionesNuevas',  numero: p.relacionesNuevas,    label: 'relaciones nuevas',    paleta: PALETAS.azul, unidad: ['relación', 'relaciones'] },
    p.filasSinCarteraLider > 0 &&
    { clave: 'SinCarteraLider',   numero: p.filasSinCarteraLider, label: 'sin cartera/líder',   paleta: PALETAS.gris },
    { clave: 'Advertencias',      numero: totalAdvertencias,     label: 'advertencias',
      paleta: totalAdvertencias > 0 ? PALETAS.ambar : PALETAS.gris, unidad: ['aviso', 'avisos'] },
  ];
  return chips.filter((c): c is ChipDef => c !== false);
}

export function PreviewCargue({ job, onConfirmar, onDescartar, confirmando, descartando }: {
  job: CargueJob;
  onConfirmar: () => void;
  onDescartar: () => void;
  confirmando: boolean;
  descartando: boolean;
}) {
  const p = job.preview;
  if (!p) return <div style={cajaError}>El servidor no devolvió la vista previa del cargue.</div>;

  const totalAdvertencias = p.advertencias.reduce((s, a) => s + a.cantidad, 0);
  const faltanColumnas = p.columnasFaltantes.length > 0;
  const puedeConfirmar = job.puedeConfirmar && !faltanColumnas && !confirmando && !descartando;

  return (
    <div style={{ display: 'flex', flexDirection: 'column', gap: 16 }}>
      {faltanColumnas && (
        <div style={cajaError}>
          <strong>Faltan columnas obligatorias:</strong> {p.columnasFaltantes.join(', ')}. No se puede confirmar este archivo.
          <div style={{ marginTop: 6, fontSize: 12 }}>
            Columnas encontradas en el archivo: {p.columnasEncontradas.join(', ') || '(ninguna)'}
          </div>
        </div>
      )}

      <DesgloseChips
        key={job.jobId}
        chips={chipsDelPreview(p, totalAdvertencias)}
        desglose={p.desglose}
        abiertosAlInicio={['Excluidas', 'Advertencias']}
      />

      <p style={{ fontSize: 12, color: 'var(--z-text-muted)', margin: 0, lineHeight: 1.6 }}>
        Toca un chip para ver de qué se compone y filas de ejemplo.
        {' '}Candidatas: <strong>{num(p.candidatas)}</strong>
        {p.agregadasPorExpansion > 0 && (
          <> (incluye <strong>{num(p.agregadasPorExpansion)}</strong> filas agregadas al expandir, una por placa)</>
        )}
      </p>

      <ListaAgrupada titulo="Información" items={p.info} tono="info" />

      {p.columnasOpcionalesFaltantes.length > 0 && (
        <Aviso tipo="warning">
          El archivo no trae las columnas opcionales <strong>{p.columnasOpcionalesFaltantes.join(', ')}</strong>:
          esos datos quedarán vacíos en las filas cargadas.
        </Aviso>
      )}
      {p.nombresIrrecuperables > 0 && (
        <Aviso tipo="warning">
          <strong>{num(p.nombresIrrecuperables)}</strong> nombre{p.nombresIrrecuperables !== 1 ? 's' : ''} llegaron
          dañados sin forma de repararlos (traen "?"). Suele pasar con un CSV guardado como ANSI: si puedes,
          vuelve a exportarlo como <strong>CSV UTF-8</strong> y súbelo de nuevo.
        </Aviso>
      )}

      <MuestraFilas preview={p} />

      <Aviso tipo="info">
        Las filas quedarán con fecha de consulta <strong>{fechaLegible(p.selloPrevisto)}</strong>.
      </Aviso>

      <div style={{ display: 'flex', gap: 10, flexWrap: 'wrap' }}>
        <button onClick={onConfirmar} disabled={!puedeConfirmar} style={btnPrimario(puedeConfirmar)}>
          {confirmando ? 'Confirmando…' : 'Confirmar carga'}
        </button>
        <button onClick={onDescartar} disabled={confirmando || descartando} style={btnSecundario}>
          {descartando ? 'Descartando…' : 'Descartar'}
        </button>
      </div>
    </div>
  );
}

// Columnas DINÁMICAS: cada fuente trae las suyas, así que el encabezado sale del servidor.
function MuestraFilas({ preview }: { preview: Preview }) {
  if (preview.columnasMuestra.length === 0 || preview.muestra.length === 0) return null;
  return (
    <div style={{ display: 'flex', flexDirection: 'column', gap: 8 }}>
      <p style={{ fontSize: 12, color: 'var(--z-text-muted)', margin: 0 }}>
        Muestra — primeras {num(preview.muestra.length)} filas ya limpias de {num(preview.candidatas)} candidatas
      </p>
      <div style={{ overflow: 'auto', maxHeight: 360, border: '1px solid var(--z-border)', borderRadius: 8 }}>
        <table style={{ width: '100%', borderCollapse: 'collapse' }}>
          <thead style={{ position: 'sticky', top: 0, background: 'var(--z-bg-card)', zIndex: 1 }}>
            <tr>{preview.columnasMuestra.map(c => <th key={c} style={TH}>{c}</th>)}</tr>
          </thead>
          <tbody>
            {preview.muestra.map((fila, i) => (
              <tr key={i}>
                {preview.columnasMuestra.map((c, j) => {
                  const v = fila[j];
                  return (
                    <td key={c} style={v == null ? { ...TD, color: 'var(--z-text-muted)' } : TD}>
                      {v ?? '—'}
                    </td>
                  );
                })}
              </tr>
            ))}
          </tbody>
        </table>
      </div>
    </div>
  );
}
