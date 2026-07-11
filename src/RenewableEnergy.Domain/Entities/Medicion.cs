using RenewableEnergy.Domain.Common;

namespace RenewableEnergy.Domain.Entities
{
    /// <summary>
    /// ENTIDAD HIJA del agregado ProyectoRenovable. Una lectura puntual de
    /// energía generada (p. ej. "el 5 de marzo el parque produjo 1.240 kWh").
    ///
    /// FÍJATE: su factory <see cref="Create"/> es <c>internal</c>. Eso obliga
    /// a que las mediciones SOLO puedan nacer a través de
    /// <c>ProyectoRenovable.RegistrarMedicion()</c> — la raíz del agregado —
    /// que es quien valida las reglas de negocio. Un handler de Application
    /// no puede hacer <c>Medicion.Create(...)</c> porque está en otro assembly.
    /// </summary>
    public sealed class Medicion : BaseEntity
    {
        /// <summary>FK al proyecto al que pertenece la medición.</summary>
        public Guid ProyectoRenovableId { get; private set; }

        /// <summary>Momento (UTC) al que corresponde la lectura.</summary>
        public DateTime FechaMedicion { get; private set; }

        /// <summary>Energía generada en kilovatios-hora. Nunca negativa.</summary>
        public decimal EnergiaGeneradaKWh { get; private set; }

        // Constructor privado para EF Core.
        private Medicion()
        {
        }

        /// <summary>
        /// Factory INTERNA: solo la puede invocar el agregado ProyectoRenovable
        /// (mismo assembly). Valida las invariantes propias de la medición.
        /// </summary>
        internal static Medicion Create(Guid proyectoId, DateTime fechaMedicion, decimal energiaGeneradaKWh)
        {
            if (energiaGeneradaKWh < 0)
            {
                throw new DomainException(
                    $"La energía generada no puede ser negativa. Recibido: {energiaGeneradaKWh} kWh");
            }

            return new Medicion
            {
                ProyectoRenovableId = proyectoId,
                FechaMedicion = fechaMedicion,
                EnergiaGeneradaKWh = energiaGeneradaKWh
            };
        }
    }
}
