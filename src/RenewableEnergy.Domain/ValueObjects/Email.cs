using System.Text.RegularExpressions;
using RenewableEnergy.Domain.Common;

namespace RenewableEnergy.Domain.ValueObjects
{
    /// <summary>
    /// VALUE OBJECT que representa una dirección de correo válida.
    ///
    /// Mismo razonamiento que <see cref="Coordenadas"/>: si un método recibe
    /// un <c>Email</c> en lugar de un <c>string</c>, tiene la GARANTÍA de que
    /// ya está validado y normalizado (minúsculas, sin espacios). La validación
    /// ocurre UNA vez, en el único punto de construcción.
    /// </summary>
    public sealed partial record Email
    {
        /// <summary>Dirección normalizada (trim + minúsculas).</summary>
        public string Valor { get; }

        private Email(string valor)
        {
            Valor = valor;
        }

        /// <summary>
        /// Crea un email validado y normalizado. Lanza
        /// <see cref="DomainException"/> si el formato no es válido.
        /// </summary>
        public static Email Create(string valor)
        {
            if (string.IsNullOrWhiteSpace(valor))
            {
                throw new DomainException("El email no puede estar vacío.");
            }

            var normalizado = valor.Trim().ToLowerInvariant();

            if (!FormatoEmail().IsMatch(normalizado))
            {
                throw new DomainException($"El email '{valor}' no tiene un formato válido.");
            }

            return new Email(normalizado);
        }

        // Regex generada en COMPILACIÓN (source generator de .NET 7+):
        // más rápida que new Regex(...) y sin coste de arranque.
        [GeneratedRegex(@"^[^@\s]+@[^@\s]+\.[^@\s]+$")]
        private static partial Regex FormatoEmail();
    }
}
