namespace RenewableEnergy.Domain.Enums
{
    /// <summary>
    /// Tipos de energía renovable que soporta la plataforma.
    ///
    /// ¿POR QUÉ UN ENUM Y NO STRINGS?
    /// - El compilador impide valores inválidos ("Solr" no compila).
    /// - En base de datos se guarda como string legible (conversión
    ///   configurada con Fluent API en ProyectoConfiguration), lo que hace
    ///   las tablas auto-explicativas sin sacrificar el tipado fuerte.
    ///
    /// Se asignan valores explícitos empezando en 1 para que el valor por
    /// defecto (0) sea SIEMPRE inválido y detectemos DTOs sin inicializar.
    /// </summary>
    public enum TipoEnergia
    {
        /// <summary>Fotovoltaica o termosolar.</summary>
        Solar = 1,

        /// <summary>Aerogeneradores terrestres u offshore.</summary>
        Eolica = 2,

        /// <summary>Centrales hidroeléctricas y minihidráulica.</summary>
        Hidraulica = 3,

        /// <summary>Combustión o gasificación de biomasa.</summary>
        Biomasa = 4,

        /// <summary>Aprovechamiento del calor del subsuelo.</summary>
        Geotermica = 5
    }
}
