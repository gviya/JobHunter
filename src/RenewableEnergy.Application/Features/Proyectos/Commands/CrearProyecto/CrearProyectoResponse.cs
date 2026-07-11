namespace RenewableEnergy.Application.Features.Proyectos.Commands.CrearProyecto
{
    /// <summary>
    /// DTO de SALIDA del caso de uso. Es lo que la API devuelve al cliente.
    ///
    /// REGLA IMPORTANTE: la API NUNCA devuelve entidades de dominio. Si se
    /// devolviera ProyectoRenovable directamente:
    /// - Se expondría el modelo interno (romperlo = romper clientes).
    /// - Se serializarían navegaciones perezosas y detalles internos.
    /// Los enums viajan como string ("Solar") para que el JSON sea legible.
    /// El mapeo lo hace AutoMapper (ver Mappings/ProyectoMappingProfile).
    /// </summary>
    public sealed record CrearProyectoResponse
    {
        public Guid Id { get; init; }
        public string Nombre { get; init; } = string.Empty;
        public string Tipo { get; init; } = string.Empty;
        public string Estado { get; init; } = string.Empty;
        public decimal PotenciaKW { get; init; }
    }
}
