using Zentinela.Application.DTOs.Virtualidad.Mensajeria;

namespace Zentinela.Application.Interfaces;

public interface IVirtualidadMensajeriaService
{
    byte[]                          GenerarPlantilla();
    Task<ValidarMensajeriaResponse> ValidarExcelAsync(Stream excelStream, int carteraID);
    Task<int>                       ConfirmarAsync(List<FilaMensajeriaDto> filas);
    Task<InformeMensajeriaDto>      GetInformeAsync(DateOnly desde, DateOnly hasta, int? liderID, int? carteraID);
}
