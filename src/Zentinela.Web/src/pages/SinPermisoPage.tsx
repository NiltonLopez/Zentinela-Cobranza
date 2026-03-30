import { useNavigate } from 'react-router-dom';

export function SinPermisoPage() {
  const navigate = useNavigate();
  return (
    <div className="min-h-screen flex items-center justify-center bg-gray-100">
      <div className="text-center">
        <h1 className="text-4xl font-bold text-gray-800 mb-2">403</h1>
        <p className="text-gray-500 mb-4">No tienes permiso para acceder a esta página.</p>
        <button
          onClick={() => navigate(-1)}
          className="text-sm text-blue-600 hover:underline"
        >
          Volver
        </button>
      </div>
    </div>
  );
}
