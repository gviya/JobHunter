using RenewableEnergy.Application.Features.Mediciones.Queries.ObtenerMediciones;

namespace RenewableEnergy.Application.Features.Proyectos.Queries.ObtenerProyectoPorId
{
    /// <summary>
    /// VIEWMODEL de detalle: la vista COMPLETA de un proyecto, incluidas sus
    /// colecciones hijas y un dato CALCULADO (TotalEnergiaGeneradaKWh) que no
    /// existe en la base de datos — se computa en el mapeo.
    /// </summary>
    public sealed record ProyectoDetalleViewModel
    {
        public Guid Id { get; init; }
        public string Nombre { get; init; } = string.Empty;
        public string? Descripcion { get; init; }
        public string Tipo { get; init; } = string.Empty;
        public string Estado { get; init; } = string.Empty;
        public decimal PotenciaKW { get; init; }
        public double Latitud { get; init; }
        public double Longitud { get; init; }
        public DateTime? FechaActivacion { get; init; }
        public DateTime CreatedAt { get; init; }
        public DateTime? UpdatedAt { get; init; }

        /// <summary>Suma de todas las mediciones. Campo calculado en el mapping.</summary>
        public decimal TotalEnergiaGeneradaKWh { get; init; }

        public IReadOnlyList<MedicionViewModel> Mediciones { get; init; } = [];
        public IReadOnlyList<InstalacionViewModel> Instalaciones { get; init; } = [];
    }

    /// <summary>Proyección de una instalación dentro del detalle del proyecto.</summary>
    public sealed record InstalacionViewModel
    {
        public Guid Id { get; init; }
        public string Nombre { get; init; } = string.Empty;
        public decimal PotenciaKW { get; init; }
    }
}
