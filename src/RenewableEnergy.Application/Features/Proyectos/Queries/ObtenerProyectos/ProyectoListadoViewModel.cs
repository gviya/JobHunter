namespace RenewableEnergy.Application.Features.Proyectos.Queries.ObtenerProyectos
{
    /// <summary>
    /// VIEWMODEL de listado (convención de nombres: {Entidad}{Operacion}ViewModel).
    ///
    /// Es una PROYECCIÓN: solo los campos que la pantalla de listado necesita.
    /// Nada de mediciones ni instalaciones — para eso está el detalle. Menos
    /// datos = respuesta más ligera y sin riesgo de lazy loading accidental.
    /// </summary>
    public sealed record ProyectoListadoViewModel
    {
        public Guid Id { get; init; }
        public string Nombre { get; init; } = string.Empty;
        public string Tipo { get; init; } = string.Empty;
        public string Estado { get; init; } = string.Empty;
        public decimal PotenciaKW { get; init; }
        public double Latitud { get; init; }
        public double Longitud { get; init; }
        public DateTime CreatedAt { get; init; }
    }
}
