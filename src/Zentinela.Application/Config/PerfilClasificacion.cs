namespace Zentinela.Application.Config;

/// <summary>
/// Clasifica el estado de un titular (UltimoEstadoTitular) en las siete categorías
/// estandarizadas del Comparador de Carteras (ADR-029, sustituye a ADR-017).
///
/// Las reglas se evalúan EN ORDEN y la primera que aplica gana (categorías excluyentes).
/// El diseño busca que las 6 categorías "reales" cubran la mayor parte de los datos y
/// que Otros quede como bucket residual mínimo (perfil sin mapear y sin FechaPromesa):
///   1. YaPago          → MejorPerfil de paz y salvo / ya pagó.
///   2. Promesa         → FechaPromesa vigente (año pasado o actual, dinámico)
///                        O MejorPerfil de acuerdo/compromiso de pago CON
///                        FechaUltimaGestion también vigente (misma ventana;
///                        confirmado por negocio 2026-07-24: un acuerdo viejo
///                        sin gestión reciente ya no pesa como Promesa).
///   3. Tercero         → MejorPerfil alusivo a contacto con tercero o familiar.
///   4. SinContacto     → MejorPerfil de marcación sin contacto, O un caso
///                        incompleto de "sin gestión" (perfil vacío/"sin gestión"
///                        pero que no cumple las 3 condiciones de SinGestion).
///   5. ContactoDirecto → MejorPerfil de contacto con el titular (TODOS, sin
///                        filtrar por fecha) O un acuerdo/compromiso de pago
///                        ya no vigente (regla 2) O cualquier FechaPromesa que
///                        llegue hasta aquí sin ser vigente (una promesa vieja
///                        implica que sí hubo contacto directo en algún momento).
///   6. SinGestion      → MejorPerfil y UltimoPerfil sin gestión (o vacíos) Y
///                        Rotación 0 — el único caso realmente "nunca tocado".
///   7. Otros           → MejorPerfil con un valor real no mapeado a ninguna
///                        categoría y sin FechaPromesa. Revisar en QA para
///                        agregar el código faltante a algún set.
///
/// MODIFICAR AQUÍ para agregar, quitar o mover perfiles entre categorías.
/// La comparación es case-insensitive; las listas incluyen códigos vigentes
/// (ver <see cref="Zentinela.Application.Tipificaciones"/>) y strings legacy.
/// </summary>
public static class PerfilClasificacion
{
    /// <summary>Titulares ya cancelados.</summary>
    public static readonly HashSet<string> YaPago = new(StringComparer.OrdinalIgnoreCase)
    {
        "Paz y salvo",
        "Consignación por aplicar",
        "Ya pago",
        "Ya pagó",
        "Pago reflejado",
        "Pago  reflejado", // así aparece en la BD (doble espacio)
        "Caso especial",
        // legacy
        "Pago Total",
        "Normalizado",
    };

    /// <summary>
    /// Perfiles de acuerdo/compromiso: clasifican como Promesa aunque no tengan
    /// FechaPromesa vigente (regla inclusiva confirmada por negocio).
    /// </summary>
    public static readonly HashSet<string> Acuerdos = new(StringComparer.OrdinalIgnoreCase)
    {
        "Acuerdo de pago",
        "Acuerdo de pago total",
        "Acuerdo de pago abono",
        "Compromiso de pago total",
        "Compromiso de abono",
        "En acuerdo",
        "Gestion Whatsapp / Promesa de Pago",
        // legacy
        "Promesa de Pago",
        "Acuerdo",
    };

    /// <summary>
    /// Contacto con el propio titular. Entra completo sin filtrar por fecha de
    /// gestión (la vigencia solo aplica a Promesa); ver también el catch-all de
    /// FechaPromesa vencida en <see cref="Clasificar"/>.
    /// Incluye "Fraude o suplantación" y "No gestionar" por decisión de negocio.
    /// </summary>
    public static readonly HashSet<string> ContactoDirecto = new(StringComparer.OrdinalIgnoreCase)
    {
        "Sin acuerdo",
        "No hubo acuerdo",
        "Renuente",
        "Fraude o suplantacion",
        "Fraude o suplantación",
        "Fraude o suplantacion de identidad",
        "No gestionar",
        "Volver a llamar",
        "Realizo abono",
        "Abonando",
        "Recordar",
        "Incumplido",
        "Potencial acuerdo",
        "Negociación vigente",
        "PQR",
        "Cliente pacta plan de pago",
        "Posible acuerdo",
        "Compromiso de pago",
        "Compromiso de pago con campaña(descuento)",
        "Abonado",
        "Recordacion de pago",
        "Recordatorio",
        "Recordatorio pago",
        "Corre fecha",
        "Solicitud de información",
        "Solicitud De Informacion",
        "Recordatorio de pago",
        "Refinanciacion sin pago",
        "Pago no reflejado",
        // legacy
        "Contacto Positivo",
    };

    /// <summary>Marcaciones que no lograron contacto con el titular ni con terceros.</summary>
    public static readonly HashSet<string> SinContacto = new(StringComparer.OrdinalIgnoreCase)
    {
        "Sin contacto",
        "No contacto",
        "Mensaje en buzón",
        "Mensaje en buzon",
        "Mensaje contestadora/buzon",
        "Numero equivocado",
        "Número equivocado",
        "Equivocado",
        "Ilocalizado",
        "Fallecido",
        "Datos de localización",
        "Mensaje de voz",
        "Ilocalizado total",
        "Datos localización",
        "Datos Localizacion",
        "No contestan",
        "Correo",
        "SMS",
        "Carteo",
        "Reclamación o derecho de petición",
        "Reclamacion o derecho de peticion",
        "IVR",
        "Se deja mensaje en contestadora",
        "Numero fuera de servicio",
        "Linea errada",
        "Ilocalizado parcial",
        "No contactado",
        "Solicitud Masivo",
        "Se solicita carta",
        "No contesta",
        "Fuera de servicio",
        "Envio masivo : WPP-Mail",
        "Telefono ocupado",
        "Datos de Localizacion",
        "Cliente Fallecido",
        "Contestar Buzon",
        "Se Dejo Mensaje",
        "No Se Dejo Mensaje",
        "No lo conocen",
        "Informativo Chat",
        "Gestion Whatsapp / No Intencion Pago",
    };

    /// <summary>Valores de perfil que cuentan como "sin gestión" (además de null/vacío).</summary>
    public static readonly HashSet<string> SinGestion = new(StringComparer.OrdinalIgnoreCase)
    {
        "Sin gestion",
        "Sin gestión",
    };

    /// <summary>
    /// Clasifica una fila de UltimoEstadoTitular. Ver el orden de reglas en el
    /// comentario de la clase; la primera regla que aplica gana.
    /// </summary>
    public static CategoriaPerfil Clasificar(
        string? mejorPerfil,
        string? ultimoPerfil,
        DateOnly? fechaPromesa,
        int? rotacion,
        DateOnly? fechaUltimaGestion,
        DateOnly hoy)
    {
        var mejor = string.IsNullOrWhiteSpace(mejorPerfil) ? null : mejorPerfil.Trim();
        var esAcuerdo = mejor is not null && Acuerdos.Contains(mejor);

        if (mejor is not null && YaPago.Contains(mejor))
            return CategoriaPerfil.YaPago;

        if (EsVigente(fechaPromesa, hoy) || (esAcuerdo && EsVigente(fechaUltimaGestion, hoy)))
            return CategoriaPerfil.Promesa;

        if (mejor is not null &&
            (mejor.Contains("tercero", StringComparison.OrdinalIgnoreCase)
          || mejor.Contains("familiar", StringComparison.OrdinalIgnoreCase)))
            return CategoriaPerfil.Tercero;

        // "Sin gestión" completo exige perfil+perfil+rotación a la vez; si falta
        // una pieza (p. ej. rotación > 0, o solo uno de los dos perfiles vacío)
        // se trata como intento de marcación sin contacto, no como Otros.
        var esSinGestionCompleto =
            EsSinGestion(mejor) && EsSinGestion(ultimoPerfil) && (rotacion ?? 0) == 0;

        if ((mejor is not null && SinContacto.Contains(mejor))
            || (EsSinGestion(mejor) && !esSinGestionCompleto))
            return CategoriaPerfil.SinContacto;

        // Un acuerdo/compromiso ya no vigente (cayó de la regla de Promesa arriba)
        // sigue siendo evidencia de contacto directo, no se pierde a Otros.
        if ((mejor is not null && (ContactoDirecto.Contains(mejor) || esAcuerdo)) || fechaPromesa.HasValue)
            return CategoriaPerfil.ContactoDirecto;

        if (esSinGestionCompleto)
            return CategoriaPerfil.SinGestion;

        return CategoriaPerfil.Otros;
    }

    /// <summary>Sobrecarga con la fecha del sistema; la explícita existe para tests.</summary>
    public static CategoriaPerfil Clasificar(
        string? mejorPerfil, string? ultimoPerfil, DateOnly? fechaPromesa, int? rotacion,
        DateOnly? fechaUltimaGestion)
        => Clasificar(mejorPerfil, ultimoPerfil, fechaPromesa, rotacion, fechaUltimaGestion,
                      DateOnly.FromDateTime(DateTime.Today));

    /// <summary>
    /// Ventana de vigencia dinámica de FechaPromesa: del 1 de enero del año
    /// pasado al 31 de diciembre del año actual, calculada siempre respecto a
    /// "hoy" (nunca años quemados).
    /// </summary>
    private static bool EsVigente(DateOnly? fecha, DateOnly hoy) =>
        fecha.HasValue && fecha.Value.Year >= hoy.Year - 1 && fecha.Value.Year <= hoy.Year;

    private static bool EsSinGestion(string? perfil) =>
        string.IsNullOrWhiteSpace(perfil) || SinGestion.Contains(perfil.Trim());

    /// <summary>
    /// Peso ordinal para el Comparador: coincide con el valor del enum.
    /// Un titular se muestra cuando su peso en otra cartera es estrictamente
    /// mayor que en la cartera seleccionada.
    /// </summary>
    public static int Peso(CategoriaPerfil c) => (int)c;

    /// <summary>Etiqueta estandarizada que ven el frontend y los Excel.</summary>
    public static string Etiqueta(CategoriaPerfil c) => c switch
    {
        CategoriaPerfil.YaPago          => "Ya pago",
        CategoriaPerfil.Promesa         => "Promesa",
        CategoriaPerfil.ContactoDirecto => "Contacto directo",
        CategoriaPerfil.Tercero         => "Tercero",
        CategoriaPerfil.SinContacto     => "Sin contacto",
        CategoriaPerfil.SinGestion      => "Sin gestión",
        _                               => "Otros",
    };
}

/// <summary>
/// Categorías estandarizadas de gestión, ordenadas de peor a mejor.
/// El valor numérico ES el peso del comparador.
/// </summary>
public enum CategoriaPerfil
{
    Otros           = 0,
    SinGestion      = 1,
    SinContacto     = 2,
    Tercero         = 3,
    ContactoDirecto = 4,
    Promesa         = 5,
    YaPago          = 6,
}
