using MediatR;
using RenewableEnergy.Application.Common.Models;
using RenewableEnergy.Domain.Enums;

namespace RenewableEnergy.Application.Features.Proyectos.Queries.ObtenerProyectos
{
    /// <summary>
    /// QUERY (lado de lectura de CQRS): listado paginado de proyectos con
    /// filtros opcionales.
    ///
    /// DIFERENCIA COMMAND vs QUERY (la pregunta clásica de entrevista):
    /// - Un COMMAND modifica el sistema y puede fallar por reglas de negocio.
    /// - Una QUERY solo LEE: no cambia estado, no levanta eventos, y puede
    ///   optimizarse agresivamente (proyecciones, caché, réplicas de lectura).
    /// </summary>
    public sealed record ObtenerProyectosQuery(
        int Pagina = 1,
        int TamanoPagina = 10,
        TipoEnergia? Tipo = null,
        bool SoloActivos = false) : IRequest<Result<PaginatedList<ProyectoListadoViewModel>>>;
}
