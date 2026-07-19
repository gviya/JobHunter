using AwesomeAssertions;
using JobHunter.Application.Features.Pipeline;
using JobHunter.Domain.Entities;

namespace JobHunter.Application.Tests.Features.Pipeline;

public sealed class CommuteFilterTests
{
    private static UserProfile MadridProfile(bool acceptHybrid = true, bool acceptOnSite = true) => new()
    {
        City = "Madrid",
        PreferredZones = ["Hortaleza", "28033", "Chamartín"],
        ExcludedAreas = ["Alcobendas", "Pozuelo", "Getafe", "Las Rozas", "Alcalá de Henares"],
        AcceptHybrid = acceptHybrid,
        AcceptOnSite = acceptOnSite,
    };

    private static JobOffer Offer(WorkMode mode, string? location) => new()
    {
        ExternalId = "1",
        SourceProvider = "Test",
        Title = "Backend Developer",
        CompanyName = "Acme",
        Url = "https://example.com/1",
        WorkMode = mode,
        Location = location,
    };

    [Theory]
    [InlineData(WorkMode.Remote, "Barcelona", true)]        // remoto pasa siempre
    [InlineData(WorkMode.Remote, null, true)]
    [InlineData(WorkMode.Unknown, "Sevilla", true)]         // sin dato de modo: no se descarta
    [InlineData(WorkMode.Hybrid, "Madrid", true)]           // híbrido en la ciudad
    [InlineData(WorkMode.Hybrid, "Madrid, España", true)]
    [InlineData(WorkMode.Hybrid, "Hortaleza, Madrid", true)] // zona preferida
    [InlineData(WorkMode.Hybrid, null, true)]                // sin localización: se deja pasar
    [InlineData(WorkMode.Hybrid, "Barcelona", false)]        // híbrido fuera de la ciudad
    [InlineData(WorkMode.Hybrid, "Alcobendas, Madrid", false)] // área excluida gana a "Madrid"
    [InlineData(WorkMode.Hybrid, "Pozuelo de Alarcón, Madrid", false)]
    [InlineData(WorkMode.Hybrid, "Alcalá de Henares", false)]
    [InlineData(WorkMode.OnSite, "Madrid", true)]            // presencial en la ciudad se acepta (puntúa bajo)
    [InlineData(WorkMode.OnSite, "Getafe, Madrid", false)]
    [InlineData(WorkMode.OnSite, "Valencia", false)]
    public void IsAcceptable_AplicaLasReglasDeDesplazamiento(WorkMode mode, string? location, bool expected)
    {
        CommuteFilter.IsAcceptable(Offer(mode, location), MadridProfile()).Should().Be(expected);
    }

    [Fact]
    public void IsAcceptable_SinCiudadEnElPerfil_NoFiltraNada()
    {
        var profile = new UserProfile();

        CommuteFilter.IsAcceptable(Offer(WorkMode.Hybrid, "Barcelona"), profile).Should().BeTrue();
        CommuteFilter.IsAcceptable(Offer(WorkMode.OnSite, "Valencia"), profile).Should().BeTrue();
    }

    [Fact]
    public void IsAcceptable_PerfilQueNoAceptaPresencial_DescartaAunqueSeaEnLaCiudad()
    {
        var profile = MadridProfile(acceptOnSite: false);

        CommuteFilter.IsAcceptable(Offer(WorkMode.OnSite, "Madrid"), profile).Should().BeFalse();
        CommuteFilter.IsAcceptable(Offer(WorkMode.Hybrid, "Madrid"), profile).Should().BeTrue();
    }

    [Fact]
    public void Apply_QuitaSoloLasNoConmutables()
    {
        List<JobOffer> offers =
        [
            Offer(WorkMode.Remote, null),
            Offer(WorkMode.Hybrid, "Madrid"),
            Offer(WorkMode.Hybrid, "Alcobendas, Madrid"),
            Offer(WorkMode.OnSite, "Barcelona"),
        ];

        var kept = CommuteFilter.Apply(offers, MadridProfile());

        kept.Should().HaveCount(2);
        kept.Should().OnlyContain(o =>
            o.WorkMode == WorkMode.Remote || (o.Location != null && o.Location == "Madrid"));
    }
}
