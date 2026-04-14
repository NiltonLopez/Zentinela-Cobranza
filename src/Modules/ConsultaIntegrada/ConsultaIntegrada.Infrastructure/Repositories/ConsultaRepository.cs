using ConsultaIntegrada.Core.Entities;
using ConsultaIntegrada.Core.Interfaces;
using ConsultaIntegrada.Infrastructure.Entities;
using Microsoft.EntityFrameworkCore;

namespace ConsultaIntegrada.Infrastructure.Repositories;

public class ConsultaRepository : IConsultaRepository
{
    private readonly ApplicationDbContext _context;

    public ConsultaRepository(ApplicationDbContext context)
    {
        _context = context;
    }

    public async Task<ConsultaResult?> ObtenerDatosPorCedulaAsync(string cedula)
    {
        if (string.IsNullOrWhiteSpace(cedula))
            return null;

        if (!long.TryParse(cedula, out var numeroDocumento))
            return null;

        var titular = await _context.Titulars
            .FirstOrDefaultAsync(t => t.NumeroDocumento == numeroDocumento);

        if (titular == null)
            return null;

        var resultado = new ConsultaResult
        {
            Cedula = cedula,
            Encontrado = true,
            FechaConsulta = DateTime.Now,
            Datos = new Dictionary<string, Dictionary<string, Dictionary<string, string>>>()
        };

        resultado.Titular = await ObtenerInfoTitularAsync(titular);

        await CargarContactabilidadAsync(titular, resultado);
        await CargarDatosPersonalesAsync(titular, resultado);
        await CargarDatosLaboralesAsync(titular, resultado);
        await CargarDatosAdicionalesAsync(titular, resultado);

        return resultado;
    }

    private static DateTime? ToDateTimeNullable(DateOnly? dateOnly)
        => dateOnly.HasValue ? dateOnly.Value.ToDateTime(TimeOnly.MinValue) : null;

    private static string FormatDateOnly(DateOnly? dateOnly)
        => dateOnly.HasValue ? dateOnly.Value.ToString("dd/MM/yyyy") : "N/A";

    private static string FormatDateTime(DateTime? dateTime)
        => dateTime?.ToString("dd/MM/yyyy") ?? "N/A";

    private async Task<TitularInfo> ObtenerInfoTitularAsync(Titular titular)
    {
        var carteras = await _context.TitularCarteras
            .Where(tc => tc.TitularId == titular.NumeroDocumento)
            .Select(tc => new CarteraInfo
            {
                NombreCartera = tc.Cartera.NombreCartera ?? "Sin nombre",
                NombreLider = tc.Lider.Nombre ?? "Sin asignar"
            })
            .ToListAsync();

        return new TitularInfo
        {
            Nombre = titular.Nombre ?? "N/A",
            NumeroDocumento = titular.NumeroDocumento.ToString(),
            FechaNacimiento = titular.FechaNacimiento,
            FechaExpedicionDocumento = titular.FechaExpedicionDocumento,
            Carteras = carteras
        };
    }

    private async Task CargarContactabilidadAsync(Titular titular, ConsultaResult resultado)
    {
        var contactabilidad = new Dictionary<string, Dictionary<string, string>>();

        var cajacopi = await _context.Cajacopis.FirstOrDefaultAsync(c => c.TitularId == titular.NumeroDocumento);
        if (cajacopi != null)
        {
            contactabilidad["Cajacopi"] = new Dictionary<string, string>
            {
                ["Número Teléfono Celular"] = cajacopi.NumeroTelefonoCelular ?? "N/A",
                ["Número Teléfono Fijo"] = cajacopi.NumeroTelefonoFijo ?? "N/A",
                ["Email"] = cajacopi.Email ?? "N/A",
                ["Departamento"] = cajacopi.Departamento ?? "N/A",
                ["Municipio"] = cajacopi.Municipio ?? "N/A",
                ["FechaConsulta"] = FormatDateOnly(cajacopi.FechaConsulta)
            };
        }

        var camara = await _context.Camaras.FirstOrDefaultAsync(c => c.TitularId == titular.NumeroDocumento);
        if (camara != null)
        {
            contactabilidad["Cámara de Comercio"] = new Dictionary<string, string>
            {
                ["Número Teléfono"] = camara.NumeroTelefono ?? "N/A",
                ["Email"] = camara.Email ?? "N/A",
                ["Municipio"] = camara.Municipio ?? "N/A",
                ["Departamento"] = camara.Departamento ?? "N/A",
                ["FechaConsulta"] = FormatDateOnly(camara.FechaConsulta)
            };
        }

        var positiva = await _context.Positivas.FirstOrDefaultAsync(p => p.TitularId == titular.NumeroDocumento);
        if (positiva != null)
        {
            contactabilidad["Positiva"] = new Dictionary<string, string>
            {
                ["Número Teléfono Celular"] = positiva.NumeroTelefonoCelular ?? "N/A",
                ["Número Teléfono Fijo"] = positiva.NumeroTelefonoFijo ?? "N/A",
                ["Email"] = positiva.Email ?? "N/A",
                ["Departamento"] = positiva.Departamento ?? "N/A",
                ["Municipio"] = positiva.Municipio ?? "N/A",
                ["FechaConsulta"] = FormatDateOnly(positiva.FechaConsulta)
            };
        }

        var saludTotal = await _context.SaludTotals.FirstOrDefaultAsync(s => s.TitularId == titular.NumeroDocumento);
        if (saludTotal != null)
        {
            contactabilidad["Salud Total"] = new Dictionary<string, string>
            {
                ["Número Teléfono"] = saludTotal.NumeroTelefono ?? "N/A",
                ["Email"] = saludTotal.Email ?? "N/A",
                ["FechaConsulta"] = FormatDateOnly(saludTotal.FechaConsulta)
            };
        }

        var tigo = await _context.Tigos.FirstOrDefaultAsync(t => t.TitularId == titular.NumeroDocumento);
        if (tigo != null)
        {
            contactabilidad["Tigo"] = new Dictionary<string, string>
            {
                ["Número Teléfono"] = tigo.NumeroTelefono ?? "N/A",
                ["Email"] = tigo.Email ?? "N/A",
                ["FechaConsulta"] = FormatDateOnly(tigo.FechaConsulta)
            };
        }

        var sos = await _context.Sos.FirstOrDefaultAsync(s => s.TitularId == titular.NumeroDocumento);
        if (sos != null)
        {
            contactabilidad["SOS"] = new Dictionary<string, string>
            {
                ["Teléfono"] = sos.Telefono ?? "N/A",
                ["Dirección"] = sos.Direccion ?? "N/A",
                ["FechaConsulta"] = FormatDateOnly(sos.FechaConsulta)
            };
        }

        if (contactabilidad.Any())
            resultado.Datos["Contactabilidad"] = contactabilidad;
    }

    private async Task CargarDatosPersonalesAsync(Titular titular, ConsultaResult resultado)
    {
        var datosPersonales = new Dictionary<string, Dictionary<string, string>>();

        var cajacopi = await _context.Cajacopis.FirstOrDefaultAsync(c => c.TitularId == titular.NumeroDocumento);
        if (cajacopi != null)
        {
            datosPersonales["Cajacopi"] = new Dictionary<string, string>
            {
                ["Fecha Nacimiento"] = FormatDateOnly(cajacopi.FechaNacimiento),
                ["Fecha Consulta"] = FormatDateOnly(cajacopi.FechaConsulta)
            };
        }

        var fosyga = await _context.Fosygas.FirstOrDefaultAsync(f => f.TitularId == titular.NumeroDocumento);
        if (fosyga != null)
        {
            datosPersonales["Fosyga"] = new Dictionary<string, string>
            {
                ["EPS"] = fosyga.Eps ?? "N/A",
                ["Régimen"] = fosyga.Regimen ?? "N/A",
                ["Fecha Afiliación Efectiva"] = FormatDateTime(ToDateTimeNullable(fosyga.FechaAfiliacionEfectiva)),
                ["Tipo Afiliado"] = fosyga.TipoAfiliado ?? "N/A",
                ["Estado"] = "Activo en Fosyga",
                ["Fecha Consulta"] = FormatDateOnly(fosyga.FechaConsulta)
            };
        }

        var positiva = await _context.Positivas.FirstOrDefaultAsync(p => p.TitularId == titular.NumeroDocumento);
        if (positiva != null)
        {
            datosPersonales["Positiva"] = new Dictionary<string, string>
            {
                ["Fecha Nacimiento"] = FormatDateOnly(positiva.FechaNacimiento),
                ["Fecha Consulta"] = FormatDateOnly(positiva.FechaConsulta)
            };
        }

        if (datosPersonales.Any())
            resultado.Datos["Datos Personales"] = datosPersonales;
    }

    private async Task CargarDatosLaboralesAsync(Titular titular, ConsultaResult resultado)
    {
        var datosLaborales = new Dictionary<string, Dictionary<string, string>>();

        var arlSura = await _context.Arlsuras.FirstOrDefaultAsync(a => a.TitularId == titular.NumeroDocumento);
        if (arlSura != null)
        {
            datosLaborales["ARLSura"] = new Dictionary<string, string>
            {
                ["NIT"] = arlSura.Nit?.ToString() ?? "N/A",
                ["Empresa"] = arlSura.Empresa ?? "N/A",
                ["Estado"] = arlSura.EstadoEnSura ? "Activo" : "No activo",
                ["Fecha Consulta"] = FormatDateOnly(arlSura.FechaConsulta)
            };
        }

        var positivaEmpresas = await _context.PositivaEmpresas.FirstOrDefaultAsync(p => p.TitularId == titular.NumeroDocumento);
        if (positivaEmpresas != null)
        {
            datosLaborales["Positiva Empresas"] = new Dictionary<string, string>
            {
                ["NIT"] = positivaEmpresas.Nit?.ToString() ?? "N/A",
                ["Empresa"] = positivaEmpresas.Empresa ?? "N/A",
                ["Estado Afiliación"] = positivaEmpresas.EstadoAfiliacion ?? "N/A",
                ["Fecha Consulta"] = FormatDateOnly(positivaEmpresas.FechaConsulta)
            };
        }

        var rues = await _context.Rues.FirstOrDefaultAsync(r => r.TitularId == titular.NumeroDocumento);
        if (rues != null)
        {
            datosLaborales["RUES"] = new Dictionary<string, string>
            {
                ["Número Matrícula"] = rues.NumeroMatricula?.ToString() ?? "N/A",
                ["Estado Empresa"] = rues.EstadoEmpresa ?? "N/A",
                ["Actividad Económica"] = rues.ActividadEconomica ?? "N/A",
                ["Nombre Empresa"] = rues.NombreEmpresa ?? "N/A",
                ["Fecha Consulta"] = FormatDateTime(rues.FechaConsulta)
            };
        }

        if (datosLaborales.Any())
            resultado.Datos["Datos Laborales"] = datosLaborales;
    }

    private async Task CargarDatosAdicionalesAsync(Titular titular, ConsultaResult resultado)
    {
        var datosAdicionales = new Dictionary<string, Dictionary<string, string>>();

        var rgm = await _context.Rgms.FirstOrDefaultAsync(r => r.TitularId == titular.NumeroDocumento);
        if (rgm != null)
        {
            datosAdicionales["RGM"] = new Dictionary<string, string>
            {
                ["Marca"] = rgm.Marca ?? "N/A",
                ["Modelo"] = rgm.Modelo?.ToString() ?? "N/A",
                ["Placa"] = rgm.Placa ?? "N/A",
                ["Fecha Consulta"] = FormatDateTime(ToDateTimeNullable(rgm.FechaConsulta))
            };
        }

        var simit = await _context.Simits.FirstOrDefaultAsync(s => s.TitularId == titular.NumeroDocumento);
        if (simit != null)
        {
            datosAdicionales["SIMIT"] = new Dictionary<string, string>
            {
                ["Placa"] = simit.Placa ?? "N/A",
                ["Estado"] = simit.EstadoEnSimit ? "Sin comparendos" : "Con comparendos",
                ["Fecha Consulta"] = FormatDateTime(ToDateTimeNullable(simit.FechaConsulta))
            };
        }

        if (datosAdicionales.Any())
            resultado.Datos["Datos Adicionales"] = datosAdicionales;
    }
}
