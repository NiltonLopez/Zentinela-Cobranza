import { useState } from 'react';
import { Link } from 'react-router-dom';
import { useQuery } from '@tanstack/react-query';
import { rastrearLinea } from '../../api/rastreoApi';
import { CargandoSpiral } from '../../components/CargandoSpiral';
import { MetricCard } from '../../components/MetricCard';
import type { RastreoTablaCard } from '../../types/rastreo';

const C = {
  brand:      '#F54927',
  brandLight: '#FEF0EC',
  success:    '#16a34a',
  warning:    '#d97706',
  danger:     '#dc2626',
  gray:       '#64748b',
};

function TarjetaTablaResultado({ card }: { card: RastreoTablaCard }) {
  const [verTodos, setVerTodos] = useState(false);
  const titulares = verTodos ? card.titulares : card.titulares.slice(0, 5);
  const restantes = card.titulares.length - 5;

  return (
    <div style={{
      background: 'var(--z-bg-card)',
      border: '0.5px solid var(--z-border)',
      borderRadius: 10,
      padding: '16px 18px',
      display: 'flex',
      flexDirection: 'column',
      gap: 10,
    }}>
      <div style={{ display: 'flex', justifyContent: 'space-between', alignItems: 'flex-start', gap: 8 }}>
        <div>
          <p style={{ fontSize: 11, fontWeight: 600, color: 'var(--z-text-muted)', textTransform: 'uppercase', letterSpacing: '0.04em', margin: 0 }}>
            Tabla
          </p>
          <p style={{ fontSize: 14, fontWeight: 600, color: 'var(--z-text-primary)', margin: '2px 0 0' }}>
            {card.etiqueta}
          </p>
        </div>
        <span style={{
          background: C.brandLight, color: C.brand,
          padding: '4px 10px', borderRadius: 12,
          fontSize: 12, fontWeight: 700, whiteSpace: 'nowrap',
        }}>
          {card.apariciones} aparición{card.apariciones === 1 ? '' : 'es'}
        </span>
      </div>

      <div style={{ borderTop: '0.5px solid var(--z-border)', paddingTop: 8, display: 'flex', flexDirection: 'column', gap: 6 }}>
        {titulares.map(t => (
          <div key={t.numeroDocumento} style={{ display: 'flex', justifyContent: 'space-between', gap: 8, fontSize: 12 }}>
            <Link
              to={`/consultas/integrada?cedula=${t.numeroDocumento}`}
              style={{ color: 'var(--z-text-primary)', textDecoration: 'none', flex: 1, overflow: 'hidden', textOverflow: 'ellipsis', whiteSpace: 'nowrap' }}
            >
              <span style={{ fontWeight: 500 }}>{t.nombre}</span>
              <span style={{ color: C.gray, marginLeft: 6 }}>· {t.numeroDocumento}</span>
            </Link>
            <span style={{ color: C.gray, fontWeight: 500, flexShrink: 0 }}>×{t.apariciones}</span>
          </div>
        ))}
        {restantes > 0 && !verTodos && (
          <button
            onClick={() => setVerTodos(true)}
            style={{ background: 'none', border: 'none', color: C.brand, fontSize: 12, fontWeight: 600, cursor: 'pointer', padding: 0, alignSelf: 'flex-start' }}
          >
            +{restantes} más
          </button>
        )}
      </div>
    </div>
  );
}

export function RastreoLineasPage() {
  const [input,   setInput]   = useState('');
  const [numero,  setNumero]  = useState<string | null>(null);
  const [errInput, setErrInput] = useState('');

  const numeroValido = input.length === 10 && /^3\d{9}$/.test(input);

  const { data, isFetching, isError, error } = useQuery({
    queryKey: ['rastreo', numero],
    queryFn:  () => rastrearLinea(numero!),
    enabled:  numero !== null,
    retry:    false,
  });

  const handleBuscar = () => {
    if (!numeroValido) {
      setErrInput('El número debe tener 10 dígitos y comenzar en 3.');
      return;
    }
    setErrInput('');
    setNumero(input);
  };

  const sinResultados = data && data.totalApariciones === 0;
  const errorMsg = isError ? (error as { response?: { data?: { mensaje?: string } } })?.response?.data?.mensaje : null;

  return (
    <div style={{ maxWidth: 1100 }}>
      {/* Encabezado */}
      <div style={{ marginBottom: 24 }}>
        <h1 style={{ fontSize: 20, fontWeight: 600, color: 'var(--z-text-primary)', margin: 0 }}>
          Rastreo de Líneas
        </h1>
        <p style={{ fontSize: 13, color: 'var(--z-text-muted)', margin: '4px 0 0' }}>
          Busca un celular en todas las tablas de contactabilidad y descubre con qué titulares está asociado.
        </p>
      </div>

      {/* Buscador */}
      <div style={{
        background: 'var(--z-bg-card)',
        border: '0.5px solid var(--z-border)',
        borderRadius: 10,
        padding: 20,
        marginBottom: 20,
        maxWidth: 900,
      }}>
        <div style={{ display: 'flex', gap: 10, alignItems: 'flex-start' }}>
          <div style={{ flex: 1 }}>
            <input
              type="text"
              inputMode="numeric"
              value={input}
              onChange={e => { setInput(e.target.value.replace(/\D/g, '').slice(0, 10)); if (errInput) setErrInput(''); }}
              onKeyDown={e => e.key === 'Enter' && handleBuscar()}
              placeholder="Número celular (10 dígitos, debe iniciar en 3)"
              maxLength={10}
              style={{
                width: '100%', padding: '8px 12px', fontSize: 14,
                border: `1px solid ${errInput ? C.danger : 'var(--z-border)'}`,
                borderRadius: 6, background: 'var(--z-bg-page)',
                color: 'var(--z-text-primary)', outline: 'none', boxSizing: 'border-box',
              }}
            />
            {errInput && <p style={{ fontSize: 11, color: C.danger, marginTop: 4 }}>{errInput}</p>}
          </div>
          <button
            onClick={handleBuscar}
            disabled={!numeroValido || isFetching}
            style={{
              padding: '8px 22px', borderRadius: 6, border: 'none',
              background: numeroValido && !isFetching ? C.brand : 'var(--z-border)',
              color: 'white', fontSize: 13, fontWeight: 600,
              cursor: numeroValido && !isFetching ? 'pointer' : 'not-allowed',
              whiteSpace: 'nowrap', flexShrink: 0,
            }}
          >
            {isFetching ? 'Rastreando…' : 'Rastrear'}
          </button>
        </div>
      </div>

      {/* Loading */}
      {isFetching && <CargandoSpiral />}

      {/* Error */}
      {isError && !isFetching && (
        <div style={{
          background: '#fef2f2',
          border: `1px solid ${C.danger}30`,
          borderLeft: `4px solid ${C.danger}`,
          borderRadius: 8,
          padding: '12px 16px',
          color: C.danger,
          fontSize: 13,
        }}>
          {errorMsg || 'Error al rastrear el número. Intenta de nuevo.'}
        </div>
      )}

      {/* Sin resultados */}
      {sinResultados && !isFetching && (
        <div style={{ textAlign: 'center', padding: '40px 20px', color: C.gray }}>
          <div style={{ fontSize: 36, opacity: 0.4, marginBottom: 12 }}>🔍</div>
          <p style={{ fontSize: 14, fontWeight: 500, margin: 0 }}>No se encontraron apariciones para <strong>{data!.numeroBuscado}</strong>.</p>
          <p style={{ fontSize: 12, margin: '4px 0 0' }}>El número no aparece en ninguna tabla de contactabilidad.</p>
        </div>
      )}

      {/* Resultados */}
      {data && data.totalApariciones > 0 && !isFetching && (
        <div style={{ display: 'flex', flexDirection: 'column', gap: 20 }}>
          {/* KPIs */}
          <div style={{ display: 'flex', gap: 12, flexWrap: 'wrap' }}>
            <div style={{ flex: 1, minWidth: 180 }}>
              <MetricCard label="Total de apariciones" value={data.totalApariciones} variant="brand" />
            </div>
            <div style={{ flex: 1, minWidth: 180 }}>
              <MetricCard label="Titulares distintos"  value={data.titularesDistintos} variant={data.titularesDistintos > 1 ? 'brand' : 'default'} />
            </div>
            <div style={{ flex: 1, minWidth: 180 }}>
              <MetricCard label="Tablas con coincidencia" value={data.porTabla.length} />
            </div>
          </div>

          {/* Aviso multititularidad */}
          {data.titularesDistintos > 1 && (
            <div style={{
              background: '#fffbeb',
              border: `1px solid ${C.warning}40`,
              borderLeft: `4px solid ${C.warning}`,
              borderRadius: 8,
              padding: '12px 16px',
              fontSize: 13,
              color: C.warning,
            }}>
              <strong>⚠ Posible multititularidad:</strong>{' '}
              el número <strong>{data.numeroBuscado}</strong> aparece asociado a {data.titularesDistintos} titulares distintos.
            </div>
          )}

          {/* Tarjetas por tabla */}
          <div>
            <h3 style={{ fontSize: 14, fontWeight: 600, color: 'var(--z-text-primary)', margin: '0 0 12px' }}>
              Apariciones por tabla
            </h3>
            <div style={{ display: 'grid', gridTemplateColumns: 'repeat(auto-fill, minmax(320px, 1fr))', gap: 12 }}>
              {data.porTabla.map(card => (
                <TarjetaTablaResultado key={card.etiqueta} card={card} />
              ))}
            </div>
          </div>

          {/* Listado de titulares asociados */}
          {data.titulares.length > 0 && (
            <div>
              <h3 style={{ fontSize: 14, fontWeight: 600, color: 'var(--z-text-primary)', margin: '0 0 12px' }}>
                Titulares asociados ({data.titulares.length})
              </h3>
              <div style={{
                background: 'var(--z-bg-card)',
                border: '0.5px solid var(--z-border)',
                borderRadius: 10,
                overflow: 'hidden',
              }}>
                {data.titulares.map((t, i) => (
                  <Link
                    key={t.numeroDocumento}
                    to={`/consultas/integrada?cedula=${t.numeroDocumento}`}
                    style={{
                      display: 'flex',
                      justifyContent: 'space-between',
                      alignItems: 'center',
                      padding: '12px 16px',
                      borderTop: i === 0 ? 'none' : '0.5px solid var(--z-border)',
                      textDecoration: 'none',
                      color: 'var(--z-text-primary)',
                      fontSize: 13,
                    }}
                  >
                    <div style={{ display: 'flex', flexDirection: 'column', gap: 2, minWidth: 0 }}>
                      <span style={{ fontWeight: 500 }}>{t.nombre}</span>
                      <span style={{ color: C.gray, fontSize: 11 }}>
                        C.C. {t.numeroDocumento} · {t.tablas.join(', ')}
                      </span>
                    </div>
                    <div style={{ display: 'flex', gap: 8, alignItems: 'center', flexShrink: 0 }}>
                      <span style={{ background: C.brandLight, color: C.brand, padding: '3px 10px', borderRadius: 12, fontSize: 11, fontWeight: 700 }}>
                        {t.tablasDondeAparece} tabla{t.tablasDondeAparece === 1 ? '' : 's'}
                      </span>
                      <span style={{ color: C.gray, fontSize: 16 }}>→</span>
                    </div>
                  </Link>
                ))}
              </div>
            </div>
          )}
        </div>
      )}
    </div>
  );
}
