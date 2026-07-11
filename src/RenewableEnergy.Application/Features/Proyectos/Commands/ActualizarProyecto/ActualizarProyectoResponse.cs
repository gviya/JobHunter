namespace RenewableEnergy.Application.Features.Proyectos.Commands.ActualizarProyecto
{
    /// <summary>DTO de salida con el estado del proyecto tras la actualización.</summary>
    public sealed record ActualizarProyectoResponse
    {
        public Guid Id { get; init; }
        public string Nombre { get; init; } = string.Empty;
        public string? Descripcion { get; init; }
        public string Tipo { get; init; } = string.Empty;
        public string Estado { get; init; } = string.Empty;
        public decimal PotenciaKW { get; init; }
        public double Latitud { get; init; }
        public double Longitud { get; init; }
    }
}
