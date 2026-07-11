using MediatR;
using RenewableEnergy.Application.Common.Models;

namespace RenewableEnergy.Application.Features.Proyectos.Commands.ActualizarProyecto
{
    /// <summary>
    /// COMMAND: "quiero actualizar los datos editables del proyecto".
    ///
    /// El Id viaja en el propio command (lo aporta la ruta HTTP:
    /// PUT /api/v1/proyectos/{id}); el resto llega en el body. Es el
    /// controller quien fusiona ambos al construir el command.
    /// El tipo de energía NO es editable: cambiar la tecnología de un
    /// proyecto es, en el negocio, crear un proyecto distinto.
    /// </summary>
    public sealed record ActualizarProyectoCommand(
        Guid Id,
        string Nombre,
        decimal PotenciaKW,
        double Latitud,
        double Longitud,
        string? Descripcion = null) : IRequest<Result<ActualizarProyectoResponse>>;
}
