namespace Zentinela.Infrastructure.SQLServer.Services.CargueFuentes.Reglas;

/// <summary>
/// Lo propio de una fuente que la definición no puede expresar (traducción de su SP legacy).
/// Todo tiene implementación por defecto: una fuente sin reglas propias usa
/// <see cref="ReglaGenerica"/> y el motor hace el resto (vacíos, teléfonos, email, lista
/// negra, flag, dedup, Titular, Titular_Cartera, refrescar/insertar, vacías).
/// </summary>
internal interface IReglaFuente
{
    string Fuente { get; }

    /// <summary>Cargar <c>PrefijoMunicipio</c> antes de limpiar (fijos de 7 dígitos).</summary>
    bool NecesitaPrefijos => false;

    /// <summary>Cruzar teléfonos contra <c>ListaNegraTelefonos</c> en la fase de cruces.</summary>
    bool NecesitaListaNegraTelefonos => false;

    /// <summary>
    /// Texto crudo de la clave de la fila (documento o, en CamaraEmpresa, NIT) ANTES de que el
    /// motor la valide (5 a 15 dígitos, se ignoran puntos y espacios). Para normalizaciones propias
    /// de la fuente: CamaraEmpresa quita el dígito de verificación de 'NIT-DV', que el motor si no
    /// pegaría al NIT. Por defecto, tal cual.
    /// </summary>
    string? NormalizarClave(string? bruto, ContextoCargue ctx) => bruto;

    /// <summary>
    /// Después del tipado genérico (trim, vacíos → NULL) y ANTES de validar celular/fijo/email:
    /// aquí se mueven o prefijan teléfonos; lo que quede mal lo anula el motor.
    /// </summary>
    void Transformar(FilaCargue fila, ContextoCargue ctx) { }

    /// <summary>Exclusiones propias, tras validar contacto (DatosClaro: sin teléfono ni email).</summary>
    void Validar(FilaCargue fila, ContextoCargue ctx) { }

    /// <summary>
    /// Una fila del archivo → N filas destino (SIMIT: una por placa). Debe devolver al menos
    /// una fila; por defecto, la misma.
    /// </summary>
    IEnumerable<FilaCargue> Expandir(FilaCargue fila, ContextoCargue ctx) => [fila];
}
