namespace RenewableEnergy.Domain.Enums
{
    /// <summary>
    /// Ciclo de vida de un proyecto renovable.
    ///
    /// Las TRANSICIONES VÁLIDAS las controla la entidad ProyectoRenovable
    /// (no los handlers ni los controllers). Este es un punto clave de DDD:
    /// el "cómo se pasa de un estado a otro" es lógica de negocio y vive
    /// en el dominio.
    ///
    ///   Borrador ──Activar()──▶ Activo ──Completar()──▶ Completado
    ///      │                      │
    ///      └──────Cancelar()──────┴──▶ Cancelado
    /// </summary>
    public enum EstadoProyecto
    {
        /// <summary>Recién creado; aún se puede modificar libremente.</summary>
        Borrador = 1,

        /// <summary>En producción: ya admite el registro de mediciones.</summary>
        Activo = 2,

        /// <summary>Detenido temporalmente (mantenimiento, incidencias...).</summary>
        Pausado = 3,

        /// <summary>Vida útil finalizada. Estado terminal.</summary>
        Completado = 4,

        /// <summary>Abandonado antes de completarse. Estado terminal.</summary>
        Cancelado = 5
    }
}
