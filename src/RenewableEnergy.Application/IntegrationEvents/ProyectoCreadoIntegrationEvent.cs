namespace RenewableEnergy.Application.IntegrationEvents
{
    /// <summary>
    /// EVENTO DE INTEGRACIÓN: versión PÚBLICA del evento de dominio
    /// ProyectoCreado, pensada para viajar por RabbitMQ/Azure Service Bus
    /// hacia otros servicios (facturación, monitorización...).
    ///
    /// ¿POR QUÉ NO PUBLICAR EL EVENTO DE DOMINIO DIRECTAMENTE?
    /// Porque el evento de dominio es un detalle INTERNO: usa tipos del
    /// dominio (TipoEnergia) y cambia al refactorizar. Este contrato usa
    /// tipos primitivos y estables. La traducción dominio → integración la
    /// hace ProyectoCreadoEventHandler.
    /// </summary>
    public sealed record ProyectoCreadoIntegrationEvent(
        Guid ProyectoId,
        string Nombre,
        string TipoEnergia,
        DateTime OcurridoEnUtc);
}
