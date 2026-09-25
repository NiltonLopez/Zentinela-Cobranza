using Zentinela.Application.DTOs.InformesLocalizacion;

namespace Zentinela.Application.Interfaces;

/// <summary>
/// Sugerencias de localización (ADR-040, etapa 2): a quién conviene mandar a buscar y en qué fuente.
/// Se calculan en línea sobre los titulares del par; no dependen de las tablas del precálculo.
/// El alcance ya viene resuelto por el controlador (Líder: su liderID y una cartera suya).
/// Lanza <see cref="LocalizacionEtlEnCursoException"/> mientras corre el ETL de gestión y
/// <see cref="ArgumentException"/> ante parámetros inválidos (fuente no Funcional, top fuera de rango…).
/// </summary>
public interface ILocalizacionSugerenciasService
{
    Task<SugerenciasLocalizacionDto> ObtenerAsync(ParametrosSugerenciasLocalizacion parametros, CancellationToken ct = default);
}
