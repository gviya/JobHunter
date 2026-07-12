using AwesomeAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using RenewableEnergy.Application.Features.Proyectos.EventHandlers;
using RenewableEnergy.Application.IntegrationEvents;
using RenewableEnergy.Application.Interfaces;
using RenewableEnergy.Domain.DomainEvents;
using RenewableEnergy.Domain.Enums;

namespace RenewableEnergy.Application.Tests.Features.Proyectos
{
    /// <summary>
    /// Tests del SUSCRIPTOR del evento ProyectoCreado: verifica las dos
    /// reacciones (email + evento de integración) y, sobre todo, la
    /// TRADUCCIÓN evento de dominio → evento de integración (los tipos del
    /// dominio no deben cruzar la frontera del servicio).
    /// </summary>
    public sealed class ProyectoCreadoEventHandlerTests
    {
        private readonly Mock<IEmailService> _emailServiceMock = new();
        private readonly Mock<IEventPublisher> _eventPublisherMock = new();
        private readonly ProyectoCreadoEventHandler _handler;

        public ProyectoCreadoEventHandlerTests()
        {
            _handler = new ProyectoCreadoEventHandler(
                _emailServiceMock.Object,
                _eventPublisherMock.Object,
                NullLogger<ProyectoCreadoEventHandler>.Instance);
        }

        [Fact]
        public async Task Handle_DeberiaEnviarEmailYPublicarEventoDeIntegracion()
        {
            var ocurrioEn = DateTime.UtcNow;
            var evento = new ProyectoCreado(Guid.NewGuid(), "Parque Eólico Test", TipoEnergia.Eolica, ocurrioEn);

            // Se captura el evento de integración publicado para inspeccionarlo.
            ProyectoCreadoIntegrationEvent? publicado = null;
            _eventPublisherMock
                .Setup(p => p.PublicarAsync(It.IsAny<ProyectoCreadoIntegrationEvent>(), It.IsAny<CancellationToken>()))
                .Callback<ProyectoCreadoIntegrationEvent, CancellationToken>((e, _) => publicado = e);

            await _handler.Handle(evento, CancellationToken.None);

            // Reacción 1: el email de notificación menciona el proyecto.
            _emailServiceMock.Verify(
                e => e.EnviarAsync(
                    It.IsAny<string>(),
                    It.Is<string>(asunto => asunto.Contains("Parque Eólico Test")),
                    It.IsAny<string>(),
                    It.IsAny<CancellationToken>()),
                Times.Once);

            // Reacción 2: la TRADUCCIÓN al contrato público es fiel y usa
            // tipos primitivos (el enum viaja como string, no como TipoEnergia).
            publicado.Should().NotBeNull();
            publicado!.ProyectoId.Should().Be(evento.ProyectoId);
            publicado.Nombre.Should().Be("Parque Eólico Test");
            publicado.TipoEnergia.Should().Be("Eolica");
            publicado.OcurridoEnUtc.Should().Be(ocurrioEn);
        }
    }
}
