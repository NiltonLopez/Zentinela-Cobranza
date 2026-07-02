namespace Zentinela.Application.DTOs.AuditoriaGestiones;

/// <summary>
/// Una gestión a evaluar por la IA: se compara el <see cref="UltimoPerfil"/>
/// (tipificación) contra el texto libre de la <see cref="Gestion"/>.
/// </summary>
public record GestionParaEvaluar(int Indice, string UltimoPerfil, string Gestion);
