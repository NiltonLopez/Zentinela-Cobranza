using Zentinela.Application.DTOs.Virtualidad.Mensajeria;

namespace Zentinela.Application.Interfaces;

public interface IVirtualidadMensajeriaService
{
    Task<ValidarMensajeriaResponse> ValidarExcelAsync(Stream excelStream);
    Task<int>                       ConfirmarAsync(List<FilaMensajeriaDto> filas);
    Task<InformeMensajeriaDto>      GetInformeAsync(DateOnly desde, DateOnly hasta);
}
