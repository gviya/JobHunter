namespace RenewableEnergy.Application.Features.Mediciones.Commands.RegistrarMedicion
{
    /// <summary>DTO de salida con la medición recién registrada.</summary>
    public sealed record RegistrarMedicionResponse
    {
        public Guid Id { get; init; }
        public Guid ProyectoId { get; init; }
        public DateTime FechaMedicion { get; init; }
        public decimal EnergiaGeneradaKWh { get; init; }
    }
}
