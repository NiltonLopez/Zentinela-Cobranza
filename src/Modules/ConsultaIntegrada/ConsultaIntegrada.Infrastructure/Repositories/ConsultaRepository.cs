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

        var tigoUne = await _context.TigoUnes.FirstOrDefaultAsync(t => t.TitularId == titular.NumeroDocumento);
        if (tigoUne != null)
        {
            contactabilidad["TigoUne"] = new Dictionary<string, string>
            {
                ["Número Teléfono"] = tigoUne.NumeroTelefono ?? "N/A",
                ["Email"] = tigoUne.Email ?? "N/A",
                ["FechaConsulta"] = FormatDateOnly(tigoUne.FechaConsulta)
            };
        }

        // Coosalud es 1:1 por TitularID (bigint) — contactabilidad + afiliación.
        var coosalud = await _context.Coosaluds.FirstOrDefaultAsync(c => c.TitularId == titular.NumeroDocumento);
        if (coosalud != null)
        {
            contactabilidad["Coosalud"] = new Dictionary<string, string>
            {
                ["Número Teléfono"]    = coosalud.NumeroTelefono   ?? "N/A",
                ["Email"]              = coosalud.Email            ?? "N/A",
                ["Régimen"]            = coosalud.Regimen          ?? "N/A",
                ["Tipo Afiliado"]      = coosalud.TipoAfiliado     ?? "N/A",
                ["Estado Afiliación"]  = coosalud.EstadoAfiliacion ?? "N/A",
                ["Nivel Sisbén"]       = coosalud.NivelSisben      ?? "N/A",
                ["Departamento"]       = coosalud.DepartamentoOrigen ?? "N/A",
                ["Municipio"]          = coosalud.MunicipioOrigen  ?? "N/A",
                ["Fecha Nacimiento"]   = coosalud.FechaNacimiento.HasValue ? FormatDateOnly(coosalud.FechaNacimiento.Value) : "N/A",
                ["Estado"]             = coosalud.EstadoEnCoosalud ? "Activo" : "Inactivo",
                ["Última Consulta"]    = FormatDateOnly(coosalud.FechaConsulta),
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

        // DatosClaro es keyless y guarda una fila por número/email encontrado del titular,
        // así que aquí traemos TODAS las filas y las aplanamos en el dict (la fuente Claro
        // es la única de Contactabilidad con cardinalidad N por titular).
        var clarosRows = await _context.DatosClaros
            .Where(c => c.TitularId == titular.NumeroDocumento)
            .ToListAsync();

        if (clarosRows.Count > 0)
        {
            var clarosOrdenados = clarosRows
                .OrderByDescending(c => c.FechaInsercion)
                .ToList();

            var numeros = clarosOrdenados
                .Select(c => c.NumeroTelefono)
                .Where(n => !string.IsNullOrWhiteSpace(n))
                .Distinct()
                .ToList();

            var emails = clarosOrdenados
                .Select(c => c.Email)
                .Where(e => !string.IsNullOrWhiteSpace(e))
                .Distinct()
                .ToList();

            var claroDict = new Dictionary<string, string>();

            if (numeros.Count == 1)
            {
                claroDict["Número Teléfono"] = numeros[0]!;
            }
            else
            {
                for (int i = 0; i < numeros.Count; i++)
                    claroDict[$"Número Teléfono {i + 1}"] = numeros[i]!;
            }

            if (emails.Count == 1)
            {
                claroDict["Email"] = emails[0]!;
            }
            else if (emails.Count > 1)
            {
                for (int i = 0; i < emails.Count; i++)
                    claroDict[$"Email {i + 1}"] = emails[i]!;
            }

            claroDict["Última Inserción"] = FormatDateOnly(clarosOrdenados[0].FechaInsercion);

            contactabilidad["Claro"] = claroDict;
        }

        // MutualSer es 1:1 por TitularID — entidad simple, sin aplanar múltiples filas.
        var mutualSer = await _context.MutualSers.FirstOrDefaultAsync(m => m.TitularId == titular.NumeroDocumento);
        if (mutualSer != null)
        {
            contactabilidad["MutualSer"] = new Dictionary<string, string>
            {
                ["Número Teléfono"]    = mutualSer.NumeroTelefonoCelular  ?? "N/A",
                ["Número Teléfono 2"]  = mutualSer.NumeroTelefonoCelular2 ?? "N/A",
                ["Teléfono Fijo"]      = mutualSer.NumeroTelefonoFijo     ?? "N/A",
                ["Email"]              = mutualSer.Email                  ?? "N/A",
                ["Régimen"]            = mutualSer.Regimen                ?? "N/A",
                ["Tipo Afiliado"]      = mutualSer.TipoAfiliado           ?? "N/A",
                ["Departamento"]       = mutualSer.Departamento           ?? "N/A",
                ["Municipio"]          = mutualSer.Municipio              ?? "N/A",
                ["Estado"]             = mutualSer.EstadoEnMutualSer ? "Activo" : "Inactivo",
                ["Última Consulta"]    = FormatDateOnly(mutualSer.FechaConsulta),
            };
        }

        // NuevaEps es 1:1 por TitularID (varchar) — se compara como string.
        var cedulaTexto = titular.NumeroDocumento.ToString();
        var nuevaEps = await _context.NuevaEpsSet.FirstOrDefaultAsync(n => n.TitularID == cedulaTexto);
        if (nuevaEps != null)
        {
            contactabilidad["NuevaEps"] = new Dictionary<string, string>
            {
                ["Número Teléfono"]    = nuevaEps.NumeroTelefono   ?? "N/A",
                ["Estado Afiliación"]  = nuevaEps.EstadoAfiliacion ?? "N/A",
                ["Tipo Afiliado"]      = nuevaEps.TipoAfiliado     ?? "N/A",
                ["Fecha Nacimiento"]   = nuevaEps.FechaNacimiento.HasValue ? FormatDateOnly(nuevaEps.FechaNacimiento.Value) : "N/A",
                ["Departamento"]       = nuevaEps.Departamento     ?? "N/A",
                ["Municipio"]          = nuevaEps.Municipio        ?? "N/A",
                ["Estado"]             = nuevaEps.EstadoEnNuevaEps ? "Activo" : "Inactivo",
                ["Última Consulta"]    = FormatDateOnly(nuevaEps.FechaConsulta),
            };
        }

        // MiVacuna es 1:1 por TitularID — entidad simple, sin aplanar múltiples filas.
        var miVacuna = await _context.MiVacunas.FirstOrDefaultAsync(m => m.TitularId == titular.NumeroDocumento);
        if (miVacuna != null)
        {
            contactabilidad["MiVacuna"] = new Dictionary<string, string>
            {
                ["Número Teléfono"]   = miVacuna.Celular ?? "N/A",
                ["Email"]             = miVacuna.Email   ?? "N/A",
                ["Fecha Expedición"]  = FormatDateOnly(miVacuna.FechaExpedicion),
                ["Estado"]            = miVacuna.EstadoEnMiVacuna == true ? "Activo" : "Inactivo",
                ["Última Consulta"]   = FormatDateOnly(miVacuna.FechaConsulta),
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

        // RGM: una fila por vehículo; deduplicamos por Placa con la consulta más reciente.
        var rgmRows = await _context.Rgms
            .Where(r => r.TitularId == titular.NumeroDocumento)
            .ToListAsync();

        if (rgmRows.Count > 0)
        {
            var vehiculos = rgmRows
                .Where(r => !string.IsNullOrWhiteSpace(r.Placa))
                .GroupBy(r => r.Placa!)
                .Select(g => g.OrderByDescending(r => r.FechaConsulta).First())
                .OrderByDescending(r => r.FechaConsulta)
                .ToList();

            if (vehiculos.Count > 0)
            {
                var rgmDict = new Dictionary<string, string>();

                if (vehiculos.Count == 1)
                {
                    var r = vehiculos[0];
                    rgmDict["Marca"] = r.Marca ?? "N/A";
                    rgmDict["Modelo"] = r.Modelo?.ToString() ?? "N/A";
                    rgmDict["Placa"] = r.Placa ?? "N/A";
                }
                else
                {
                    for (int i = 0; i < vehiculos.Count; i++)
                    {
                        var r = vehiculos[i];
                        var n = i + 1;
                        rgmDict[$"Marca {n}"] = r.Marca ?? "N/A";
                        rgmDict[$"Modelo {n}"] = r.Modelo?.ToString() ?? "N/A";
                        rgmDict[$"Placa {n}"] = r.Placa ?? "N/A";
                    }
                }

                rgmDict["Fecha Consulta"] = FormatDateTime(ToDateTimeNullable(vehiculos[0].FechaConsulta));
                datosAdicionales["RGM"] = rgmDict;
            }
        }

        // SIMIT: una fila por placa; deduplicamos por Placa con la consulta más reciente.
        var simitRows = await _context.Simits
            .Where(s => s.TitularId == titular.NumeroDocumento)
            .ToListAsync();

        if (simitRows.Count > 0)
        {
            var placas = simitRows
                .Where(s => !string.IsNullOrWhiteSpace(s.Placa))
                .GroupBy(s => s.Placa!)
                .Select(g => g.OrderByDescending(s => s.FechaConsulta).First())
                .OrderByDescending(s => s.FechaConsulta)
                .ToList();

            if (placas.Count > 0)
            {
                var simitDict = new Dictionary<string, string>();

                if (placas.Count == 1)
                {
                    var s = placas[0];
                    simitDict["Placa"] = s.Placa ?? "N/A";
                    simitDict["Estado"] = s.EstadoEnSimit ? "Sin comparendos" : "Con comparendos";
                }
                else
                {
                    for (int i = 0; i < placas.Count; i++)
                    {
                        var s = placas[i];
                        var n = i + 1;
                        simitDict[$"Placa {n}"] = s.Placa ?? "N/A";
                        simitDict[$"Estado Placa {n}"] = s.EstadoEnSimit ? "Sin comparendos" : "Con comparendos";
                    }
                }

                simitDict["Fecha Consulta"] = FormatDateTime(ToDateTimeNullable(placas[0].FechaConsulta));
                datosAdicionales["SIMIT"] = simitDict;
            }
        }

        if (datosAdicionales.Any())
            resultado.Datos["Datos Adicionales"] = datosAdicionales;
    }
}
