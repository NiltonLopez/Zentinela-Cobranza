import { useQuery } from '@tanstack/react-query';
import { useNavigate } from 'react-router-dom';
import { useAuth } from '../context/AuthContext';
import { obtenerCarteras } from '../api/carterasApi';
import { TarjetaCartera } from '../components/TarjetaCartera';
import type { CarteraDto } from '../types/carteras';

export function DashboardPage() {
  const { usuario, cerrarSesion } = useAuth();
  const navigate = useNavigate();

  const { data: carteras, isLoading, isError } = useQuery({
    queryKey: ['carteras'],
    queryFn: obtenerCarteras,
  });

  const handleSeleccionarCartera = (cartera: CarteraDto) => {
    // Navegar al generador de bases — próximo paso
    navigate(`/generador/${cartera.id}`);
  };

  return (
    <div className="min-h-screen bg-gray-50">
      {/* Header */}
      <header className="bg-white border-b border-gray-200 px-6 py-4">
        <div className="max-w-5xl mx-auto flex justify-between items-center">
          <div>
            <h1 className="text-lg font-semibold text-gray-800">Zentinela</h1>
            <p className="text-xs text-gray-400">{usuario?.nombreCompleto} · {usuario?.rol}</p>
          </div>
          <button
            onClick={cerrarSesion}
            className="text-sm text-gray-400 hover:text-red-500 transition-colors"
          >
            Cerrar sesión
          </button>
        </div>
      </header>

      {/* Contenido */}
      <main className="max-w-5xl mx-auto px-6 py-8">
        <h2 className="text-xl font-semibold text-gray-700 mb-6">Mis carteras</h2>

        {isLoading && (
          <div className="grid grid-cols-1 sm:grid-cols-2 lg:grid-cols-3 gap-4">
            {[1, 2, 3].map((n) => (
              <div key={n} className="bg-white border border-gray-200 rounded-xl p-5 animate-pulse">
                <div className="h-3 bg-gray-200 rounded w-16 mb-3" />
                <div className="h-5 bg-gray-200 rounded w-40" />
              </div>
            ))}
          </div>
        )}

        {isError && (
          <div className="bg-red-50 border border-red-200 rounded-xl p-5 text-sm text-red-600">
            No se pudieron cargar las carteras. Verifica tu conexión o intenta de nuevo.
          </div>
        )}

        {!isLoading && !isError && carteras?.length === 0 && (
          <div className="bg-white border border-gray-200 rounded-xl p-8 text-center">
            <p className="text-gray-400 text-sm">No tienes carteras asignadas.</p>
            <p className="text-gray-300 text-xs mt-1">Contacta al administrador para que te asigne una cartera.</p>
          </div>
        )}

        {!isLoading && !isError && carteras && carteras.length > 0 && (
          <div className="grid grid-cols-1 sm:grid-cols-2 lg:grid-cols-3 gap-4">
            {carteras.map((cartera) => (
              <TarjetaCartera
                key={cartera.id}
                cartera={cartera}
                onClick={handleSeleccionarCartera}
              />
            ))}
          </div>
        )}
      </main>
    </div>
  );
}
