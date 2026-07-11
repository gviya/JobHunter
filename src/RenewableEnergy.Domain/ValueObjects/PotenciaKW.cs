using RenewableEnergy.Domain.Common;

namespace RenewableEnergy.Domain.ValueObjects
{
    /// <summary>
    /// VALUE OBJECT que representa una potencia eléctrica en kilovatios.
    ///
    /// ¿POR QUÉ NO UN decimal A SECAS?
    /// Es el antídoto contra la "obsesión por los primitivos" (primitive
    /// obsession): un decimal puede ser -5, cero o 10^30; una PotenciaKW
    /// construida SIEMPRE es válida. Además el tipo documenta la UNIDAD:
    /// nadie confundirá kW con MW si el tipo lo dice.
    /// </summary>
    public sealed record PotenciaKW
    {
        /// <summary>Potencia máxima admitida: 10 GW (10.000.000 kW).</summary>
        public const decimal Maximo = 10_000_000m;

        /// <summary>Valor en kilovatios. Siempre &gt; 0 y &lt;= <see cref="Maximo"/>.</summary>
        public decimal Valor { get; }

        // Constructor privado: la única puerta de entrada es Create().
        private PotenciaKW(decimal valor)
        {
            Valor = valor;
        }

        /// <summary>
        /// Crea una potencia validada. Lanza <see cref="DomainException"/>
        /// si el valor es cero, negativo o supera el máximo.
        /// </summary>
        public static PotenciaKW Create(decimal valor)
        {
            if (valor <= 0)
            {
                throw new DomainException($"La potencia debe ser mayor que cero. Recibido: {valor} kW");
            }

            if (valor > Maximo)
            {
                throw new DomainException($"La potencia no puede superar {Maximo:N0} kW. Recibido: {valor} kW");
            }

            return new PotenciaKW(valor);
        }
    }
}
