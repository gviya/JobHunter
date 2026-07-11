namespace RenewableEnergy.API.Dto.Proyectos
{
    /// <summary>
    /// DTO de entrada del PUT /api/v1/proyectos/{id}. No lleva Id: viaja en
    /// la RUTA y el controller lo fusiona al construir el command.
    /// </summary>
    public sealed record ActualizarProyectoRequest(
        string Nombre,
        decimal PotenciaKW,
        CoordenadasRequest Coordenadas,
        string? Descripcion = null);
}
