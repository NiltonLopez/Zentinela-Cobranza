import { useAuth } from '../context/AuthContext';

export function DashboardPage() {
  const { usuario, cerrarSesion } = useAuth();

  return (
    <div className="min-h-screen bg-gray-50 p-8">
      <div className="max-w-4xl mx-auto">
        <div className="flex justify-between items-center mb-6">
          <div>
            <h1 className="text-2xl font-semibold text-gray-800">Dashboard</h1>
            <p className="text-sm text-gray-500">
              Bienvenido, {usuario?.nombreCompleto} — {usuario?.rol}
            </p>
          </div>
          <button
            onClick={cerrarSesion}
            className="text-sm text-gray-500 hover:text-red-600 transition-colors"
          >
            Cerrar sesión
          </button>
        </div>
        <div className="bg-white rounded-2xl shadow-sm border border-gray-200 p-6">
          <p className="text-gray-500 text-sm">Módulo de carteras — próximamente</p>
        </div>
      </div>
    </div>
  );
}
