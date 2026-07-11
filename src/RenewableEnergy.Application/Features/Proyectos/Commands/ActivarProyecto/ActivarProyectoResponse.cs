namespace RenewableEnergy.Application.Features.Proyectos.Commands.ActivarProyecto
{
    /// <summary>DTO de salida tras activar el proyecto.</summary>
    public sealed record ActivarProyectoResponse
    {
        public Guid Id { get; init; }
        public string Nombre { get; init; } = string.Empty;
        public string Estado { get; init; } = string.Empty;
        public DateTime? FechaActivacion { get; init; }
    }
}
