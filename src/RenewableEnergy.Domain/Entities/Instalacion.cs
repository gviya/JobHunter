using RenewableEnergy.Domain.Common;
using RenewableEnergy.Domain.ValueObjects;

namespace RenewableEnergy.Domain.Entities
{
    /// <summary>
    /// ENTIDAD HIJA del agregado ProyectoRenovable. Un elemento físico que
    /// compone el proyecto: un string de placas fotovoltaicas, un
    /// aerogenerador, una turbina...
    ///
    /// Igual que <see cref="Medicion"/>: nace SOLO a través de la raíz
    /// (<c>ProyectoRenovable.AgregarInstalacion()</c>).
    /// </summary>
    public sealed class Instalacion : BaseEntity
    {
        /// <summary>FK al proyecto al que pertenece.</summary>
        public Guid ProyectoRenovableId { get; private set; }

        /// <summary>Nombre identificativo, p. ej. "Aerogenerador AG-07".</summary>
        public string Nombre { get; private set; } = string.Empty;

        /// <summary>Potencia de esta instalación concreta.</summary>
        public PotenciaKW PotenciaInstalada { get; private set; } = null!;

        // Constructor privado para EF Core.
        private Instalacion()
        {
        }

        /// <summary>Factory interna: solo invocable desde el agregado.</summary>
        internal static Instalacion Create(Guid proyectoId, string nombre, PotenciaKW potenciaInstalada)
        {
            if (string.IsNullOrWhiteSpace(nombre))
            {
                throw new DomainException("El nombre de la instalación es obligatorio.");
            }

            return new Instalacion
            {
                ProyectoRenovableId = proyectoId,
                Nombre = nombre.Trim(),
                PotenciaInstalada = potenciaInstalada
            };
        }
    }
}
