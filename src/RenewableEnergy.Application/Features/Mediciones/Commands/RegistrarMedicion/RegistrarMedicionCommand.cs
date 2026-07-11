using MediatR;
using RenewableEnergy.Application.Common.Models;

namespace RenewableEnergy.Application.Features.Mediciones.Commands.RegistrarMedicion
{
    /// <summary>
    /// COMMAND: "registrar una lectura de energía en un proyecto".
    ///
    /// Este es el caso de uso que la guía docs/GUIA_NUEVAS_FEATURES.md
    /// construye paso a paso — si quieres aprender a añadir una feature
    /// nueva a esta plantilla, sigue esa guía con estos archivos delante.
    /// </summary>
    public sealed record RegistrarMedicionCommand(
        Guid ProyectoId,
        DateTime FechaMedicion,
        decimal EnergiaGeneradaKWh) : IRequest<Result<RegistrarMedicionResponse>>;
}
