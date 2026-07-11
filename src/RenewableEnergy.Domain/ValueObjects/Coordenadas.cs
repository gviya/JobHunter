using RenewableEnergy.Domain.Common;

namespace RenewableEnergy.Domain.ValueObjects
{
    /// <summary>
    /// VALUE OBJECT que representa una posición geográfica (latitud/longitud).
    ///
    /// ¿QUÉ ES UN VALUE OBJECT?
    /// Un objeto SIN identidad propia: dos coordenadas con los mismos valores
    /// SON la misma coordenada (igualdad por valor, no por Id). Al ser un
    /// `record`, C# genera la igualdad por valor automáticamente.
    ///
    /// ¿POR QUÉ NO USAR DOS double SUELTOS?
    /// 1. VALIDACIÓN CENTRALIZADA: es IMPOSIBLE construir una Coordenadas
    ///    inválida (latitud 200 no existe). La regla vive aquí, no repartida
    ///    por validators y controllers.
    /// 2. EXPRESIVIDAD: `Coordenadas ubicacion` comunica más que
    ///    `double lat, double lon` (y evita intercambiar los argumentos).
    /// 3. INMUTABILIDAD: una vez creada no cambia; para "mover" un proyecto
    ///    se crea una coordenada nueva. Sin efectos colaterales.
    ///
    /// En base de datos se persiste como OWNED TYPE de EF Core (dos columnas
    /// en la tabla del proyecto). Ver ProyectoConfiguration en Infrastructure.
    /// </summary>
    public sealed record Coordenadas
    {
        /// <summary>Latitud en grados decimales. Rango válido: -90 a 90.</summary>
        public double Latitud { get; }

        /// <summary>Longitud en grados decimales. Rango válido: -180 a 180.</summary>
        public double Longitud { get; }

        // Constructor privado: obliga a pasar por Create(), el único punto
        // donde se validan las reglas. EF Core también usa este constructor
        // (por convención, casando nombres de parámetros con propiedades).
        private Coordenadas(double latitud, double longitud)
        {
            Latitud = latitud;
            Longitud = longitud;
        }

        /// <summary>
        /// Único punto de creación (patrón Factory Method). Lanza
        /// <see cref="DomainException"/> si los valores están fuera de rango.
        /// </summary>
        public static Coordenadas Create(double latitud, double longitud)
        {
            if (latitud is < -90 or > 90)
            {
                throw new DomainException($"La latitud debe estar entre -90 y 90 grados. Recibido: {latitud}");
            }

            if (longitud is < -180 or > 180)
            {
                throw new DomainException($"La longitud debe estar entre -180 y 180 grados. Recibido: {longitud}");
            }

            return new Coordenadas(latitud, longitud);
        }
    }
}
