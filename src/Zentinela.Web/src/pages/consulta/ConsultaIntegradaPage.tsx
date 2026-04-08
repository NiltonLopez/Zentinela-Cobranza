import { useState } from 'react';
import { useQuery } from '@tanstack/react-query';
import { consultarPorCedula } from '../../api/consultaApi';
import type { TitularInfo } from '../../types/consulta';
import { CargandoSpiral } from '../../components/CargandoSpiral';

// ── Helpers ───────────────────────────────────────────────────────────────────

function formatearFecha(valor: string | null | undefined): string {
  if (!valor) return '—';
  const f = new Date(valor);
  if (isNaN(f.getTime())) return valor;
  return f.toLocaleDateString('es-CO', { day: '2-digit', month: '2-digit', year: 'numeric' });
}

function esBadgeActivo(valor: string): boolean {
  const v = valor.toLowerCase();
  return v.includes('activo') || v.includes('afiliado') || v.includes('registrado') || v.includes('sin comparendo');
}

function esBadgeInactivo(valor: string): boolean {
  const v = valor.toLowerCase();
  return v.includes('inactivo') || v.includes('no activo') || v.includes('retirado') || v.includes('con comparendo');
}

function esFecha(campo: string): boolean {
  return campo.toLowerCase().includes('fecha');
}

function esEmail(campo: string): boolean {
  return campo.toLowerCase().includes('email') || campo.toLowerCase().includes('correo');
}

// ── Sub-componentes ───────────────────────────────────────────────────────────

function ValorCampo({ campo, valor }: { campo: string; valor: string }) {
  if (!valor || valor === '0' || valor === '') {
    return <span style={{ color: 'var(--z-text-muted)', fontSize: '12px' }}>—</span>;
  }

  if (esBadgeActivo(valor)) {
    return (
      <span style={{
        display: 'inline-block', padding: '2px 8px', borderRadius: '100px',
        fontSize: '11px', fontWeight: 600, background: '#dcfce7', color: '#16a34a',
        border: '1px solid #bbf7d0',
      }}>{valor}</span>
    );
  }

  if (esBadgeInactivo(valor)) {
    return (
      <span style={{
        display: 'inline-block', padding: '2px 8px', borderRadius: '100px',
        fontSize: '11px', fontWeight: 600, background: '#fee2e2', color: '#dc2626',
        border: '1px solid #fecaca',
      }}>{valor}</span>
    );
  }

  if (esEmail(campo) && valor.includes('@')) {
    return (
      <a href={`mailto:${valor}`} style={{ fontSize: '12px', color: 'var(--z-brand)', textDecoration: 'none' }}>
        {valor}
      </a>
    );
  }

  if (esFecha(campo)) {
    return <span style={{ fontSize: '12px', color: 'var(--z-text-primary)' }}>{formatearFecha(valor)}</span>;
  }

  return <span style={{ fontSize: '12px', color: 'var(--z-text-primary)' }}>{valor}</span>;
}

function Subseccion({ nombre, campos }: { nombre: string; campos: Record<string, string> }) {
  const [abierta, setAbierta] = useState(true);
  const entradas = Object.entries(campos).filter(([, v]) => v && v !== '0' && v !== '');

  return (
    <div style={{ border: '0.5px solid var(--z-border)', borderRadius: '8px', overflow: 'hidden' }}>
      <button
        onClick={() => setAbierta(p => !p)}
        style={{
          width: '100%', display: 'flex', alignItems: 'center', justifyContent: 'space-between',
          padding: '9px 14px', background: 'var(--z-bg-page)', border: 'none', cursor: 'pointer',
          textAlign: 'left',
        }}
      >
        <span style={{ fontSize: '12px', fontWeight: 600, color: 'var(--z-text-primary)' }}>{nombre}</span>
        <span style={{
          fontSize: '10px', color: 'var(--z-text-muted)',
          display: 'inline-block',
          transform: abierta ? 'rotate(180deg)' : 'none', transition: 'transform 0.15s',
        }}>▼</span>
      </button>

      {abierta && (
        <div style={{
          display: 'grid', gridTemplateColumns: 'repeat(auto-fill, minmax(200px, 1fr))',
          gap: '8px 16px', padding: '12px 14px',
          borderTop: '0.5px solid var(--z-border)',
        }}>
          {entradas.length === 0 ? (
            <p style={{ fontSize: '12px', color: 'var(--z-text-muted)', gridColumn: '1 / -1' }}>
              Sin datos disponibles.
            </p>
          ) : entradas.map(([campo, valor]) => (
            <div key={campo}>
              <p style={{
                fontSize: '10px', fontWeight: 600, color: 'var(--z-text-muted)',
                textTransform: 'uppercase', letterSpacing: '0.04em', marginBottom: '2px',
              }}>{campo}</p>
              <ValorCampo campo={campo} valor={valor} />
            </div>
          ))}
        </div>
      )}
    </div>
  );
}

function Grupo({ nombre, fuentes }: { nombre: string; fuentes: Record<string, Record<string, string>> }) {
  const [abierto, setAbierto] = useState(true);

  return (
    <div style={{
      background: 'var(--z-bg-card)', border: '0.5px solid var(--z-border)',
      borderRadius: '10px', overflow: 'hidden', marginBottom: '12px',
    }}>
      <button
        onClick={() => setAbierto(p => !p)}
        style={{
          width: '100%', display: 'flex', alignItems: 'center', justifyContent: 'space-between',
          padding: '14px 20px', background: 'none', border: 'none', cursor: 'pointer', textAlign: 'left',
        }}
      >
        <span style={{ fontSize: '14px', fontWeight: 600, color: 'var(--z-text-primary)' }}>{nombre}</span>
        <span style={{
          fontSize: '11px', color: 'var(--z-text-muted)', display: 'inline-block',
          transform: abierto ? 'rotate(180deg)' : 'none', transition: 'transform 0.15s',
        }}>▼</span>
      </button>

      {abierto && (
        <div style={{ padding: '0 16px 16px', display: 'flex', flexDirection: 'column', gap: '8px' }}>
          {Object.entries(fuentes).map(([fuente, campos]) => (
            <Subseccion key={fuente} nombre={fuente} campos={campos} />
          ))}
        </div>
      )}
    </div>
  );
}

function TarjetaTitular({ titular }: { titular: TitularInfo }) {
  return (
    <div style={{
      background: 'var(--z-bg-card)', border: '0.5px solid var(--z-border)',
      borderRadius: '10px', padding: '20px', marginBottom: '16px',
    }}>
      <div style={{ display: 'flex', alignItems: 'flex-start', justifyContent: 'space-between', flexWrap: 'wrap', gap: '8px', marginBottom: '16px' }}>
        <div>
          <h3 style={{ fontSize: '16px', fontWeight: 600, color: 'var(--z-text-primary)', marginBottom: '2px' }}>
            {titular.nombre}
          </h3>
          <p style={{ fontSize: '13px', color: 'var(--z-text-muted)' }}>
            C.C. {titular.numeroDocumento}
          </p>
        </div>
        <span style={{
          padding: '3px 10px', borderRadius: '100px', fontSize: '11px', fontWeight: 600,
          background: 'var(--z-brand-light)', color: 'var(--z-brand)', border: '0.5px solid #f5cbb8',
          letterSpacing: '0.04em', textTransform: 'uppercase', whiteSpace: 'nowrap',
        }}>
          Titular encontrado
        </span>
      </div>

      <div style={{
        display: 'grid', gridTemplateColumns: 'repeat(auto-fill, minmax(180px, 1fr))',
        gap: '12px 20px', marginBottom: titular.carteras.length > 0 ? '16px' : 0,
      }}>
        <div>
          <p style={{ fontSize: '10px', fontWeight: 600, color: 'var(--z-text-muted)', textTransform: 'uppercase', letterSpacing: '0.04em', marginBottom: '2px' }}>
            Fecha de nacimiento
          </p>
          <p style={{ fontSize: '13px', color: 'var(--z-text-primary)' }}>
            {formatearFecha(titular.fechaNacimiento)}
          </p>
        </div>
        <div>
          <p style={{ fontSize: '10px', fontWeight: 600, color: 'var(--z-text-muted)', textTransform: 'uppercase', letterSpacing: '0.04em', marginBottom: '2px' }}>
            Expedición documento
          </p>
          <p style={{ fontSize: '13px', color: 'var(--z-text-primary)' }}>
            {formatearFecha(titular.fechaExpedicionDocumento)}
          </p>
        </div>
      </div>

      {titular.carteras.length > 0 && (
        <>
          <p style={{ fontSize: '11px', fontWeight: 600, color: 'var(--z-text-muted)', textTransform: 'uppercase', letterSpacing: '0.04em', marginBottom: '8px' }}>
            Carteras asignadas
          </p>
          <div style={{ display: 'flex', flexWrap: 'wrap', gap: '8px' }}>
            {titular.carteras.map((c, i) => (
              <div key={i} style={{
                padding: '8px 12px', borderRadius: '8px',
                background: 'var(--z-bg-page)', border: '0.5px solid var(--z-border)',
              }}>
                <p style={{ fontSize: '12px', fontWeight: 600, color: 'var(--z-text-primary)', marginBottom: '2px' }}>
                  {c.nombreCartera}
                </p>
                <p style={{ fontSize: '11px', color: 'var(--z-text-muted)' }}>
                  Líder: {c.nombreLider}
                </p>
              </div>
            ))}
          </div>
        </>
      )}
    </div>
  );
}

// ── Página principal ──────────────────────────────────────────────────────────

export function ConsultaIntegradaPage() {
  const [inputCedula,   setInputCedula]   = useState('');
  const [cedulaBuscada, setCedulaBuscada] = useState<string | null>(null);
  const [errorInput,    setErrorInput]    = useState('');

  const { data, isFetching, isError, error } = useQuery({
    queryKey: ['consulta', cedulaBuscada],
    queryFn: () => consultarPorCedula(cedulaBuscada!),
    enabled: cedulaBuscada !== null,
    retry: false,
  });

  const handleBuscar = () => {
    const cedula = inputCedula.trim();
    if (!cedula) {
      setErrorInput('Ingresa un número de cédula.');
      return;
    }
    if (cedula.length < 6 || cedula.length > 10) {
      setErrorInput('La cédula debe tener entre 6 y 10 dígitos.');
      return;
    }
    setErrorInput('');
    setCedulaBuscada(cedula);
  };

  const handleInputChange = (e: React.ChangeEvent<HTMLInputElement>) => {
    setInputCedula(e.target.value.replace(/[^0-9]/g, ''));
    if (errorInput) setErrorInput('');
  };

  const es404 = isError && (error as { response?: { status: number } })?.response?.status === 404;

  return (
    <div style={{ maxWidth: '900px' }}>

      {/* Encabezado */}
      <div style={{ marginBottom: '24px' }}>
        <h2 style={{ fontSize: '18px', fontWeight: 600, color: 'var(--z-text-primary)', marginBottom: '4px' }}>
          Consulta Integrada
        </h2>
        <p style={{ fontSize: '13px', color: 'var(--z-text-muted)' }}>
          Busca información de un titular en todas las fuentes disponibles.
        </p>
      </div>

      {/* Buscador */}
      <div style={{
        background: 'var(--z-bg-card)', border: '0.5px solid var(--z-border)',
        borderRadius: '10px', padding: '20px', marginBottom: '20px',
      }}>
        <div style={{ display: 'flex', gap: '10px', alignItems: 'flex-start' }}>
          <div style={{ flex: 1 }}>
            <input
              type="text"
              inputMode="numeric"
              value={inputCedula}
              onChange={handleInputChange}
              onKeyDown={e => e.key === 'Enter' && handleBuscar()}
              placeholder="Número de cédula (6–10 dígitos)"
              maxLength={10}
              style={{
                width: '100%', padding: '8px 12px', fontSize: '14px',
                border: `1px solid ${errorInput ? '#dc2626' : 'var(--z-border)'}`,
                borderRadius: '6px', background: 'var(--z-bg-page)',
                color: 'var(--z-text-primary)', outline: 'none', boxSizing: 'border-box',
              }}
            />
            {errorInput && (
              <p style={{ fontSize: '11px', color: '#dc2626', marginTop: '4px' }}>{errorInput}</p>
            )}
          </div>
          <button
            onClick={handleBuscar}
            disabled={isFetching}
            style={{
              padding: '8px 22px', borderRadius: '6px', border: 'none',
              background: 'var(--z-brand)', color: 'white',
              fontSize: '13px', fontWeight: 600,
              cursor: isFetching ? 'not-allowed' : 'pointer',
              opacity: isFetching ? 0.7 : 1, whiteSpace: 'nowrap', flexShrink: 0,
            }}
          >
            {isFetching ? 'Consultando...' : 'Buscar'}
          </button>
        </div>
      </div>

      {/* Cargando */}
      {isFetching && (
        <div style={{
          background: 'var(--z-bg-card)', border: '0.5px solid var(--z-border)',
          borderRadius: '10px',
        }}>
          <CargandoSpiral mensaje="Consultando en todas las bases de datos..." />
        </div>
      )}

      {/* No encontrado */}
      {!isFetching && es404 && (
        <div style={{
          background: 'var(--z-bg-card)', border: '0.5px solid var(--z-border)',
          borderRadius: '10px', padding: '32px', textAlign: 'center',
        }}>
          <p style={{ fontSize: '14px', fontWeight: 600, color: 'var(--z-text-primary)', marginBottom: '4px' }}>
            Sin resultados
          </p>
          <p style={{ fontSize: '13px', color: 'var(--z-text-muted)' }}>
            La cédula {cedulaBuscada} no existe en ninguna base de datos.
          </p>
        </div>
      )}

      {/* Error genérico */}
      {!isFetching && isError && !es404 && (
        <div style={{
          background: '#fff5f5', border: '0.5px solid #fecaca',
          borderRadius: '10px', padding: '16px 20px',
        }}>
          <p style={{ fontSize: '13px', color: '#dc2626', fontWeight: 600 }}>
            Error al consultar. Intenta de nuevo.
          </p>
        </div>
      )}

      {/* Resultados */}
      {!isFetching && data?.encontrado && (
        <>
          {data.titular && <TarjetaTitular titular={data.titular} />}
          {Object.entries(data.datos).map(([grupo, fuentes]) => (
            <Grupo key={grupo} nombre={grupo} fuentes={fuentes} />
          ))}
        </>
      )}

    </div>
  );
}
