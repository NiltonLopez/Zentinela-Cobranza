namespace Zentinela.Application.DTOs.CreadorZentinela;

public enum TipoColumna { Texto, Numero, Fecha }

public record ColumnaBaseMadre(string Nombre, string Etiqueta, TipoColumna Tipo);
