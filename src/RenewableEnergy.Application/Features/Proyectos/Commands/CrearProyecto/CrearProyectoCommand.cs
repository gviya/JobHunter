using MediatR;
using RenewableEnergy.Application.Common.Models;
using RenewableEnergy.Domain.Enums;

namespace RenewableEnergy.Application.Features.Proyectos.Commands.CrearProyecto
{
    /// <summary>
    /// COMMAND (lado de escritura de CQRS): "quiero crear un proyecto".
    ///
    /// Es un `record` posicional e INMUTABLE: una vez construido, nadie puede
    /// alterar sus datos mientras viaja por el pipeline. Transporta SOLO
    /// primitivos y enums (nada de entidades de dominio): es un mensaje.
    ///
    /// Implementa IRequest&lt;Result&lt;CrearProyectoResponse&gt;&gt;: el tipo de
    /// retorno declara en la firma que la operación puede fallar (patrón
    /// Result) y qué devuelve si va bien.
    /// </summary>
    public sealed record CrearProyectoCommand(
        string Nombre,
        TipoEnergia Tipo,
        decimal PotenciaKW,
        double Latitud,
        double Longitud,
        string? Descripcion = null) : IRequest<Result<CrearProyectoResponse>>;
}
