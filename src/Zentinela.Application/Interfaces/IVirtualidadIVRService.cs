using Zentinela.Application.DTOs.Virtualidad.IVR;

namespace Zentinela.Application.Interfaces;

public interface IVirtualidadIVRService
{
    Task<ValidarIVRResponse> ValidarPreEnvioAsync(Stream excelStream);
    Task<int>                ConfirmarPreEnvioAsync(List<FilaIVRDto> filas);
    Task<ValidarIVRResponse> ValidarPostEnvioAsync(Stream excelStream);
    Task<int>                ConfirmarPostEnvioAsync(List<FilaIVRDto> filas);
    Task<InformeIVRDto>      GetInformeAsync(int? carteraID, DateOnly desde, DateOnly hasta);
}
