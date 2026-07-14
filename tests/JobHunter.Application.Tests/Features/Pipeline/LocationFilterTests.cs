using AwesomeAssertions;
using JobHunter.Application.Features.Pipeline;

namespace JobHunter.Application.Tests.Features.Pipeline;

public sealed class LocationFilterTests
{
    [Theory]
    [InlineData("Madrid", true)]
    [InlineData("Barcelona, España", true)]
    [InlineData("Remote", true)]
    [InlineData("Remote - EMEA", true)]
    [InlineData(null, true)]
    [InlineData("", true)]
    [InlineData("   ", true)]
    [InlineData("Delhi, India", false)]
    [InlineData("Remote - US Only", false)]
    [InlineData("New York", false)]
    [InlineData("Remote - Mexico Only", false)]
    [InlineData("Remote - Argentina Only", false)]
    [InlineData("Pakistan", false)]
    [InlineData("Lahore", false)]
    [InlineData("Basingstoke, UK", false)]
    [InlineData("London, UK", false)]
    [InlineData("UK - Remote", false)]
    [InlineData("Paris, France (hybrid)", false)]
    [InlineData("Tel-Aviv, Israel", false)]
    [InlineData("Tokyo, Japan", false)]
    [InlineData("Warsaw", false)]
    [InlineData("Cyprus", false)]
    [InlineData("Anywhere; Armenia; Georgia; Latvia; Moldova", false)]
    public void IsSpainCompatible_ClasificaLaUbicacionCorrectamente(string? location, bool expected)
    {
        LocationFilter.IsSpainCompatible(location).Should().Be(expected);
    }

    [Fact]
    public void IsSpainCompatible_MencionaEspanaAunqueTambienDigaRemoteUsOnly_AceptaPorEspana()
    {
        // España gana aunque en el mismo texto aparezca un marcador ajeno:
        // la coincidencia con SpainKeywords se comprueba antes que el denylist.
        LocationFilter.IsSpainCompatible("Remote - Spain or US").Should().BeTrue();
    }

    [Fact]
    public void IsSpainCompatible_ListaDePaisesConEspanaIncluida_Acepta()
    {
        // Lever a veces expresa el alcance como una lista de países separada
        // por ";". Si España está en la lista, el rol es válido aunque
        // también se ofrezca a otros países.
        LocationFilter.IsSpainCompatible("Armenia; Cyprus; Poland; Spain").Should().BeTrue();
    }

    [Fact]
    public void IsSpainCompatible_ListaDePaisesSinEspana_Rechaza()
    {
        LocationFilter.IsSpainCompatible("Anywhere; Armenia; Georgia; Latvia; Moldova").Should().BeFalse();
    }
}
