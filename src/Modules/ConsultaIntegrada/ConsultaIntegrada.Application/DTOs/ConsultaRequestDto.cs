namespace ConsultaIntegrada.Application.DTOs
{
    public class ConsultaRequestDto
    {
        public string Cedula { get; set; } = string.Empty;
    }

    public class ConsultaResponseDto
    {
        public bool Exitoso { get; set; }
        public string Mensaje { get; set; } = string.Empty;
        public object? Data { get; set; }
    }
}
