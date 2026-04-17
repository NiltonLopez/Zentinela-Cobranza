using Zentinela.Application.DTOs.Virtualidad.LlamadasPerdidas;

namespace Zentinela.Application.Interfaces;

public interface IVirtualidadLlamadasService
{
    Task<ValidarLlamadasResponse> ValidarExcelAsync(Stream excelStream);
    Task<int>                     ConfirmarAsync(List<FilaLlamadaDto> filas);
    Task<InformeLlamadasDto>      GetInformeAsync();
}
