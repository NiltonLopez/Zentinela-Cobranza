import { useAuth } from '../context/AuthContext';

/**
 * Alcance por rol en los informes. Un Líder ve el informe tal cual el administrador, pero solo
 * con sus datos: el selector de líder no se muestra y el filtro queda fijado a su propio liderID.
 * Esto es presentación — la restricción real la impone el servidor, que para el rol Líder ignora
 * el liderID de la URL y usa el del token (AlcanceLiderExtensions).
 *
 * Lo comparten Informes de Virtualidad y de Localización (ADR-040).
 */
export function useAlcanceLider() {
  const { usuario } = useAuth();
  const esLider = usuario?.rol === 'Líder';
  return { esLider, liderPropio: esLider ? (usuario?.liderID ?? 0) : 0 };
}
