import { useRef, useState } from 'react';
import { useMutation, useQuery } from '@tanstack/react-query';
import { CargandoSpiral } from '../../../components/CargandoSpiral';
import { DropZone, ChipConteo, ListaErroresAgrupados, TH, TD, btnPrimario, btnSecundario, cajaError } from '../../administracion/historico-virtualidad/compartidos';
import { descargarPlantillaSms, obtenerCarterasListaNegra, validarArchivoSms, mensajeDeErrorBlob } from '../../../api/creadorVirtualidadApi';
import type { EstadoAsistente, Accion } from './estadoAsistente';
import { C, etiquetaPaso, Aviso, BarraAcciones } from './compartidosSms';

interface Props {
  estado: EstadoAsistente;
  dispatch: React.Dispatch<Accion>;
  liderListo: boolean;
}

export function PasoCargarArchivo({ estado, dispatch, liderListo }: Props) {
  const inputRef   = useRef<HTMLInputElement>(null);
  const inputLnRef = useRef<HTMLInputElement>(null);
  const [progreso, setProgreso]   = useState(0);
  const [generando, setGenerando] = useState(false);
  const [error, setError]         = useState<string | null>(null);
  const [verErrores, setVerErrores] = useState(false);

  const { data: carteras = [], isLoading: cargandoCarteras } = useQuery({
    queryKey: ['sms-lista-negra-carteras'],
    queryFn:  obtenerCarterasListaNegra,
    staleTime: 10 * 60 * 1000,
  });

  const validar = useMutation({
    mutationFn: () => validarArchivoSms(
      { archivo: estado.archivo!, liderID: estado.liderID, carterasListaNegra: estado.carterasListaNegra, archivoListaNegra: estado.archivoListaNegra },
      setProgreso,
    ),
    onMutate:  () => { setError(null); setProgreso(0); },
    onSuccess: v => dispatch({ tipo: 'SET_VALIDACION', validacion: v }),
    onError:   async e => setError(await mensajeDeErrorBlob(e, 'No se pudo analizar el archivo.')),
  });

  const descargarPlantilla = async () => {
    setGenerando(true);
    try { await descargarPlantillaSms(); } finally { setGenerando(false); }
  };

  const toggleCartera = (valor: string) => {
    const set = new Set(estado.carterasListaNegra);
    if (set.has(valor)) set.delete(valor); else set.add(valor);
    dispatch({ tipo: 'SET_CARTERAS_LN', carteras: [...set] });
  };
  const todasCarteras = () => dispatch({ tipo: 'SET_CARTERAS_LN', carteras: carteras.map(c => c.valor) });
  const ningunaCartera = () => dispatch({ tipo: 'SET_CARTERAS_LN', carteras: [] });

  const v = estado.validacion;
  const puedeValidar = !!estado.archivo && liderListo && !validar.isPending;
  const totalExcluidos = v
    ? v.excluidosCedulaInvalida + v.excluidosTelefonoInvalido + v.excluidosFijo + v.excluidosNombreVacio
      + v.excluidosPerfilArchivo + v.excluidosPerfilBD + v.excluidosListaNegraTabla + v.excluidosListaNegraArchivo + v.excluidosDuplicado
    : 0;

  const chip = (numero: number, label: string, color: string, bg: string) => numero > 0 && (
    <ChipConteo numero={numero} label={label} bg={bg} border={color + '55'} colorNum={color} colorLabel={color} />
  );

  return (
    <div style={{ display: 'flex', flexDirection: 'column', gap: 22 }}>

      {/* Plantilla */}
      <div style={{ display: 'flex', flexDirection: 'column', gap: 8 }}>
        <p style={etiquetaPaso}>Paso 1 — Descarga la plantilla</p>
        <div style={{ display: 'flex', alignItems: 'center', gap: 12, flexWrap: 'wrap' }}>
          <button onClick={descargarPlantilla} disabled={generando} style={btnSecundario}>
            {generando ? 'Generando…' : '↓ Descargar plantilla'}
          </button>
          <span style={{ fontSize: 12, color: 'var(--z-text-muted)' }}>
            Columnas: <strong>Cédula · Nombre · Fecha Gestión · Ultimo Perfil · Numero Marcado</strong> y, opcionales,
            <strong> Cartera · Empresa</strong> (solo si el mensaje usa {'{CARTERA}'} o {'{EMPRESA}'}). Recomendado XLSX
            (en CSV una coma dentro del nombre corre la fila).
          </span>
        </div>
      </div>

      {/* Lista negra */}
      <div style={{ display: 'flex', flexDirection: 'column', gap: 8 }}>
        <p style={etiquetaPaso}>Paso 2 — Carteras de lista negra a cruzar</p>
        {cargandoCarteras ? (
          <span style={{ fontSize: 12, color: 'var(--z-text-muted)' }}>Cargando carteras…</span>
        ) : (
          <>
            <div style={{ display: 'flex', gap: 8, fontSize: 12 }}>
              <button onClick={todasCarteras}   style={{ ...btnSecundario, padding: '4px 10px' }}>Todas</button>
              <button onClick={ningunaCartera}  style={{ ...btnSecundario, padding: '4px 10px' }}>Ninguna</button>
              <span style={{ color: 'var(--z-text-muted)', alignSelf: 'center' }}>
                {estado.carterasListaNegra.length} de {carteras.length} seleccionadas
              </span>
            </div>
            <div style={{ display: 'flex', flexWrap: 'wrap', gap: 6 }}>
              {carteras.map(c => {
                const on = estado.carterasListaNegra.includes(c.valor);
                return (
                  <button key={c.valor} onClick={() => toggleCartera(c.valor)} title={`${c.totalNumeros.toLocaleString('es-CO')} números`}
                    style={{
                      padding: '5px 10px', borderRadius: 999, fontSize: 12, cursor: 'pointer',
                      border: `1px solid ${on ? C.sms : 'var(--z-border)'}`,
                      background: on ? '#ede9fe' : 'var(--z-bg-page)',
                      color: on ? C.sms : 'var(--z-text-muted)', fontWeight: on ? 600 : 400,
                    }}>
                    {c.etiqueta}
                  </button>
                );
              })}
            </div>
            {estado.carterasListaNegra.length === 0 && (
              <Aviso tipo="warning">Sin carteras seleccionadas <strong>no se cruza la lista negra de la base de datos</strong>. Solo aplicará la lista por archivo, si la subes.</Aviso>
            )}
          </>
        )}

        <div style={{ display: 'flex', alignItems: 'center', gap: 10, marginTop: 4, flexWrap: 'wrap' }}>
          <span style={{ fontSize: 12, color: 'var(--z-text-muted)' }}>Lista negra adicional por archivo (opcional, columna de teléfono):</span>
          <input ref={inputLnRef} type="file" accept=".xlsx,.csv" style={{ display: 'none' }}
            onChange={e => dispatch({ tipo: 'SET_ARCHIVO_LN', archivo: e.target.files?.[0] ?? null })} />
          <button onClick={() => inputLnRef.current?.click()} style={{ ...btnSecundario, padding: '5px 12px' }}>
            {estado.archivoListaNegra ? 'Cambiar' : 'Subir archivo'}
          </button>
          {estado.archivoListaNegra && (
            <span style={{ fontSize: 12 }}>
              {estado.archivoListaNegra.name}
              <button onClick={() => { dispatch({ tipo: 'SET_ARCHIVO_LN', archivo: null }); if (inputLnRef.current) inputLnRef.current.value = ''; }}
                style={{ marginLeft: 8, background: 'none', border: 'none', color: C.danger, cursor: 'pointer', fontSize: 12 }}>× quitar</button>
            </span>
          )}
        </div>
      </div>

      {/* Archivo */}
      <div style={{ display: 'flex', flexDirection: 'column', gap: 8 }}>
        <p style={etiquetaPaso}>Paso 3 — Sube el archivo diligenciado</p>
        <DropZone
          archivoNombre={estado.archivo?.name ?? ''}
          archivoKB={estado.archivo ? Math.round(estado.archivo.size / 1024) : 0}
          inputRef={inputRef}
          accept=".xlsx,.csv"
          textoVacio="Arrastra o haz clic para subir el archivo (.xlsx o .csv)"
          onClick={() => inputRef.current?.click()}
          onChange={e => dispatch({ tipo: 'SET_ARCHIVO', archivo: e.target.files?.[0] ?? null })}
          onQuitar={() => { dispatch({ tipo: 'SET_ARCHIVO', archivo: null }); if (inputRef.current) inputRef.current.value = ''; }}
          disabled={validar.isPending}
        />
        {!liderListo && <Aviso tipo="warning">Selecciona el líder antes de analizar el archivo.</Aviso>}
        <div>
          <button onClick={() => validar.mutate()} disabled={!puedeValidar} style={btnPrimario(puedeValidar)}>
            {validar.isPending ? 'Analizando…' : v ? 'Volver a analizar' : 'Analizar archivo'}
          </button>
        </div>
        {validar.isPending && <CargandoSpiral mensaje={`Depurando el archivo${progreso > 0 && progreso < 100 ? ` — subiendo ${progreso}%` : '…'}`} />}
        {error && <div style={cajaError}>{error}</div>}
      </div>

      {/* Resultado de la depuración */}
      {v && (
        <div style={{ display: 'flex', flexDirection: 'column', gap: 14 }}>
          <p style={etiquetaPaso}>Resultado de la depuración</p>

          {v.columnasFaltantes.length === 0 && v.columnasOpcionalesFaltantes.length > 0 && (
            <Aviso tipo="info">
              El archivo no trae <strong>{v.columnasOpcionalesFaltantes.join(' ni ')}</strong>: los mensajes que usen esas variables aparecerán deshabilitados.
            </Aviso>
          )}
          {v.columnasFaltantes.length > 0 && (
            <Aviso tipo="danger">
              Faltan columnas obligatorias: <strong>{v.columnasFaltantes.join(', ')}</strong>.
              Columnas encontradas: {v.columnasEncontradas.join(', ') || '(ninguna)'}.
            </Aviso>
          )}

          <div style={{ display: 'flex', gap: 10, flexWrap: 'wrap' }}>
            <ChipConteo numero={v.totalLeidas}  label="leídas"  bg="var(--z-bg-page)" border="var(--z-border)" colorNum="var(--z-text-primary)" colorLabel="var(--z-text-muted)" />
            <ChipConteo numero={v.totalValidas} label="válidas para SMS" bg="#f0fdf4" border={C.success + '55'} colorNum={C.success} colorLabel={C.success} />
            {totalExcluidos > 0 && (
              <ChipConteo numero={totalExcluidos} label="excluidas (ver detalle)" bg="#fef2f2" border={C.danger + '55'} colorNum={C.danger} colorLabel={C.danger}
                onClick={() => setVerErrores(x => !x)} abierto={verErrores} />
            )}
          </div>

          <div style={{ display: 'flex', gap: 8, flexWrap: 'wrap' }}>
            {chip(v.excluidosPerfilArchivo,    'perfil no contactable (archivo)', C.warning, '#fffbeb')}
            {chip(v.excluidosPerfilBD,         'perfil no contactable (histórico BD)', C.warning, '#fffbeb')}
            {chip(v.excluidosListaNegraTabla,  'lista negra (BD)', C.danger, '#fef2f2')}
            {chip(v.excluidosListaNegraArchivo,'lista negra (archivo)', C.danger, '#fef2f2')}
            {chip(v.excluidosFijo,             'teléfono fijo', C.gray, 'var(--z-bg-page)')}
            {chip(v.excluidosTelefonoInvalido, 'teléfono inválido', C.gray, 'var(--z-bg-page)')}
            {chip(v.excluidosCedulaInvalida,   'cédula inválida', C.gray, 'var(--z-bg-page)')}
            {chip(v.excluidosNombreVacio,      'nombre vacío', C.gray, 'var(--z-bg-page)')}
            {chip(v.excluidosDuplicado,        'número repetido', C.gray, 'var(--z-bg-page)')}
          </div>

          {v.totalNumerosListaNegraArchivo > 0 && (
            <span style={{ fontSize: 12, color: 'var(--z-text-muted)' }}>
              La lista negra por archivo aportó {v.totalNumerosListaNegraArchivo.toLocaleString('es-CO')} números.
            </span>
          )}

          {verErrores && v.erroresAgrupados.length > 0 && <ListaErroresAgrupados errores={v.erroresAgrupados} />}

          {v.muestra.length > 0 && (
            <div style={{ overflowX: 'auto', border: '0.5px solid var(--z-border)', borderRadius: 8 }}>
              <table style={{ width: '100%', borderCollapse: 'collapse' }}>
                <thead><tr>{['Cédula', 'Nombre (limpio)', 'Fecha Gestión', 'Ultimo Perfil', 'Numero Marcado', 'Cartera', 'Empresa'].map(h => <th key={h} style={TH}>{h}</th>)}</tr></thead>
                <tbody>
                  {v.muestra.map(f => (
                    <tr key={`${f.cedula}-${f.numeroMarcado}`}>
                      <td style={TD}>{f.cedula}</td>
                      <td style={TD}>{f.nombre}</td>
                      <td style={TD}>{f.fechaGestion}</td>
                      <td style={TD}>{f.ultimoPerfil}</td>
                      <td style={TD}>{f.numeroMarcado}</td>
                      <td style={{ ...TD, color: f.cartera ? undefined : 'var(--z-text-muted)' }}>{f.cartera || '—'}</td>
                      <td style={{ ...TD, color: f.empresa ? undefined : 'var(--z-text-muted)' }}>{f.empresa || '—'}</td>
                    </tr>
                  ))}
                </tbody>
              </table>
              <p style={{ margin: 0, padding: '6px 10px', fontSize: 11, color: 'var(--z-text-muted)' }}>
                Muestra de {v.muestra.length} de {v.totalValidas.toLocaleString('es-CO')} filas válidas.
              </p>
            </div>
          )}

          {v.totalValidas === 0 && <Aviso tipo="danger">No quedó ningún titular válido. Revisa el detalle de exclusiones.</Aviso>}
        </div>
      )}

      <BarraAcciones>
        <button onClick={() => dispatch({ tipo: 'IR_A', paso: 2 })} disabled={!v || v.totalValidas === 0} style={btnPrimario(!!v && v.totalValidas > 0)}>
          Continuar →
        </button>
      </BarraAcciones>
    </div>
  );
}
