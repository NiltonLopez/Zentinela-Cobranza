using Zentinela.Application.DTOs.Virtualidad.IVR;

namespace Zentinela.Application.Interfaces;

public interface IVirtualidadIVRService
{
    byte[]                    GenerarPlantilla();
    Task<ValidarIVRResponse>  ValidarExcelAsync(Stream excelStream, int carteraID);
    Task<int>                 ConfirmarAsync(List<FilaIVRDto> filas);
    Task<InformeIVRDto>       GetInformeAsync(DateOnly desde, DateOnly hasta, int? liderID, int? carteraID);
}
