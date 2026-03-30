import type { CarteraDto } from '../types/carteras';

interface Props {
  cartera: CarteraDto;
  onClick: (cartera: CarteraDto) => void;
}

export function TarjetaCartera({ cartera, onClick }: Props) {
  return (
    <button
      onClick={() => onClick(cartera)}
      className="w-full text-left bg-white border border-gray-200 rounded-xl p-5 hover:border-blue-400 hover:shadow-md transition-all duration-200 group"
    >
      <div className="flex items-center justify-between">
        <div>
          <p className="text-xs text-gray-400 uppercase tracking-wide mb-1">Cartera</p>
          <h3 className="text-base font-semibold text-gray-800 group-hover:text-blue-600 transition-colors">
            {cartera.nombreCartera}
          </h3>
        </div>
        <span className="text-gray-300 group-hover:text-blue-400 transition-colors text-xl">→</span>
      </div>
    </button>
  );
}
