using RenewableEnergy.Domain.Common;

namespace RenewableEnergy.Domain.DomainEvents
{
    /// <summary>
    /// EVENTO DE DOMINIO: "se ha registrado una medición de energía".
    ///
    /// Lo levanta <c>ProyectoRenovable.RegistrarMedicion()</c> — fíjate en que
    /// lo levanta el AGREGADO (la raíz), no la entidad hija Medicion: todo
    /// cambio del agregado pasa por su raíz.
    /// </summary>
    public sealed record MedicionRegistrada(
        Guid ProyectoId,
        Guid MedicionId,
        decimal EnergiaGeneradaKWh,
        DateTime OcurridoEnUtc) : IDomainEvent;
}
