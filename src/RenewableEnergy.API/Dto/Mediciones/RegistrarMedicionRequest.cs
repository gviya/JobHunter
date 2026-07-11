namespace RenewableEnergy.API.Dto.Mediciones
{
    /// <summary>
    /// DTO de entrada del POST /api/v1/proyectos/{proyectoId}/mediciones.
    /// El proyectoId viaja en la ruta (recurso anidado REST).
    /// </summary>
    public sealed record RegistrarMedicionRequest(
        DateTime FechaMedicion,
        decimal EnergiaGeneradaKWh);
}
