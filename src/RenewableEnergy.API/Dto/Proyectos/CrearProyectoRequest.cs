using RenewableEnergy.Domain.Enums;

namespace RenewableEnergy.API.Dto.Proyectos
{
    /// <summary>
    /// DTO de ENTRADA del endpoint POST /api/v1/proyectos (el "contrato"
    /// público del body JSON).
    ///
    /// ¿POR QUÉ NO BINDEAR EL COMMAND DIRECTAMENTE?
    /// Separar request HTTP y command permite que el contrato público
    /// evolucione sin tocar Application (y viceversa), y deja claro qué
    /// campos vienen del cliente y cuáles añade el controller (p. ej. el id
    /// de la ruta en el PUT). El enum viaja como string ("Solar") gracias al
    /// JsonStringEnumConverter configurado en ServiceCollectionExtensions.
    /// </summary>
    public sealed record CrearProyectoRequest(
        string Nombre,
        TipoEnergia Tipo,
        decimal PotenciaKW,
        CoordenadasRequest Coordenadas,
        string? Descripcion = null);

    /// <summary>Sub-objeto de coordenadas del body JSON.</summary>
    public sealed record CoordenadasRequest(double Latitud, double Longitud);
}
