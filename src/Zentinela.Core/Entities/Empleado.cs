namespace Zentinela.Core.Entities;

public class Empleado
{
    public int ID { get; set; }
    public long NumeroDocumento { get; set; }
    public int TipoDocumento { get; set; }
    public string Nombre { get; set; } = string.Empty;
    public string Apellidos { get; set; } = string.Empty;
    public DateTime FechaNacimiento { get; set; }
    public char Sexo { get; set; }
    public string? RH { get; set; }
    public string? Direccion { get; set; }
    public string Ciudad { get; set; } = string.Empty;
    public string? NumeroTelefono { get; set; }
    public string? Email { get; set; }
    public DateTime? FechaIngreso { get; set; }
    public DateTime? FechaRetiro { get; set; }
    public int CargoID { get; set; }
    public int EmpresaID { get; set; }
    public decimal? SalarioBase { get; set; }
    public string? EPS { get; set; }
    public string? AFP { get; set; }
    // Relación recursiva: supervisor del empleado (FK a Empleado.ID)
    public int? IDSupervisor { get; set; }
    public bool EstadoEmpleado { get; set; }
    public int? Piso { get; set; }

    public Cargo Cargo { get; set; } = null!;
    public Empleado? Supervisor { get; set; }
}
