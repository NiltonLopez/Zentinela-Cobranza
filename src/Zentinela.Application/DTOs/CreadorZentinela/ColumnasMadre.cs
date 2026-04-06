namespace Zentinela.Application.DTOs.CreadorZentinela;

/// <summary>
/// Catálogo estático de columnas de vw_BaseMadreBaseGeneral.
/// Al agregar una nueva fuente de datos a la vista, añadir la columna aquí.
/// </summary>
public static class ColumnasMadre
{
    public static readonly IReadOnlyList<ColumnaBaseMadre> Todas =
    [
        new("NumeroDocumento",            "N° Documento",                  TipoColumna.Numero),
        new("Nombre",                     "Nombre",                        TipoColumna.Texto),
        new("Saldo",                      "Saldo",                         TipoColumna.Texto),
        new("UltimaFechaGestion",         "Última Fecha Gestión",          TipoColumna.Texto),
        new("FechaPromesa",               "Fecha Promesa",                 TipoColumna.Texto),
        new("ValorPromesa",               "Valor Promesa",                 TipoColumna.Texto),
        new("MejorPerfil",                "Mejor Perfil",                  TipoColumna.Texto),
        new("UltimoPerfil",               "Último Perfil",                 TipoColumna.Texto),
        new("Rotacion",                   "Rotación",                      TipoColumna.Texto),
        new("UltimoNumeroMarcado",        "Último Número Marcado",         TipoColumna.Texto),
        new("Departamento",               "Departamento",                  TipoColumna.Texto),
        new("Municipio",                  "Municipio",                     TipoColumna.Texto),
        new("EstadoEnFosyga",             "Estado Fosyga",                 TipoColumna.Texto),
        new("EPS",                        "EPS",                           TipoColumna.Texto),
        new("Regimen",                    "Régimen",                       TipoColumna.Texto),
        new("TipoAfiliado",               "Tipo Afiliado",                 TipoColumna.Texto),
        new("UltimaConsultaFosyga",       "Última Consulta Fosyga",        TipoColumna.Fecha),
        new("EPSEnCompensados",           "EPS Compensados",               TipoColumna.Texto),
        new("UltimoPeriodoCompensado",    "Último Período Compensado",     TipoColumna.Fecha),
        new("TipoAfiliadoCompensados",    "Tipo Afiliado Compensados",     TipoColumna.Texto),
        new("UltimaConsultaCompensados",  "Última Consulta Compensados",   TipoColumna.Fecha),
        new("EdadRegistraduria",          "Edad Registraduría",            TipoColumna.Numero),
        new("EdadCajaCopi",               "Edad CajaCopi",                 TipoColumna.Numero),
        new("EdadPositiva",               "Edad Positiva",                 TipoColumna.Numero),
        new("CelularCajacopi",            "Celular CajaCopi",              TipoColumna.Texto),
        new("EmailCajacopi",              "Email CajaCopi",                TipoColumna.Texto),
        new("CelularCamara",              "Celular Cámara",                TipoColumna.Texto),
        new("EmailCamara",                "Email Cámara",                  TipoColumna.Texto),
        new("CelularSaludTotal",          "Celular Salud Total",           TipoColumna.Texto),
        new("EmailSaludTotal",            "Email Salud Total",             TipoColumna.Texto),
        new("CelularDatosExternos",       "Celular Datos Externos",        TipoColumna.Texto),
        new("EmailDatosExternos",         "Email Datos Externos",          TipoColumna.Texto),
        new("CelularPositiva",            "Celular Positiva",              TipoColumna.Texto),
        new("EmailPositiva",              "Email Positiva",                TipoColumna.Texto),
        new("CelularTigo",                "Celular Tigo",                  TipoColumna.Texto),
        new("EmailTigo",                  "Email Tigo",                    TipoColumna.Texto),
        new("CelularSOS",                 "Celular SOS",                   TipoColumna.Texto),
        new("NITSura",                    "NIT Sura",                      TipoColumna.Numero),
        new("EmpresaSura",                "Empresa Sura",                  TipoColumna.Texto),
        new("NITPositiva",                "NIT Positiva",                  TipoColumna.Numero),
        new("EmpresaPositiva",            "Empresa Positiva",              TipoColumna.Texto),
        new("NITSaludTotal",              "NIT Salud Total",               TipoColumna.Numero),
        new("EmpresaSaludTotal",          "Empresa Salud Total",           TipoColumna.Texto),
        new("NitSOS",                     "NIT SOS",                       TipoColumna.Numero),
        new("EmpresaSOS",                 "Empresa SOS",                   TipoColumna.Texto),
        new("EmpresaRUES",                "Empresa RUES",                  TipoColumna.Texto),
        new("PlacaSIMIT",                 "Placa SIMIT",                   TipoColumna.Texto),
        new("Marca",                      "Marca (Vehículo)",              TipoColumna.Texto),
        new("Modelo",                     "Modelo (Vehículo)",             TipoColumna.Numero),
        new("Placa",                      "Placa (Vehículo)",              TipoColumna.Texto),
    ];
}
