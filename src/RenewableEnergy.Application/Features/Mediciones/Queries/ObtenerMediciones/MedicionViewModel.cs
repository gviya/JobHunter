namespace RenewableEnergy.Application.Features.Mediciones.Queries.ObtenerMediciones
{
    /// <summary>
    /// Proyección de una medición. La reutiliza también el detalle del
    /// proyecto (ProyectoDetalleViewModel.Mediciones).
    /// </summary>
    public sealed record MedicionViewModel
    {
        public Guid Id { get; init; }
        public DateTime FechaMedicion { get; init; }
        public decimal EnergiaGeneradaKWh { get; init; }
    }
}
