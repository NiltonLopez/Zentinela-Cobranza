using Zentinela.Application.DTOs.Virtualidad.LlamadasEntrantes;

namespace Zentinela.Application.Interfaces;

public interface IVirtualidadEntrantesService
{
    Task<ValidarEntrantesResponse> ValidarExcelAsync(Stream excelStream);
    Task<int>                      ConfirmarAsync(List<FilaEntranteDto> filas);
    Task<InformeEntrantesDto>      GetInformeAsync(int anio);
}
