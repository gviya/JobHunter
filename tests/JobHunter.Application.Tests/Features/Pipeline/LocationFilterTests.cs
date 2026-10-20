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

    [Theory]
    [InlineData("Remote - LATAM")]
    [InlineData("Remote (LatAm)")]
    [InlineData("Remote (Americas)")]
    [InlineData("Remote, US only")]
    [InlineData("Remote (U.S.)")]
    [InlineData("North America")]
    [InlineData("Remote - North America")]
    [InlineData("APAC")]
    [InlineData("Remote - APAC")]
    [InlineData("Remote - Asia Pacific")]
    [InlineData("Remote - Latin America")]
    [InlineData("Remote - South America")]
    [InlineData("Remoto - Latinoamérica")]
    [InlineData("Remoto (América Latina)")]
    [InlineData("Remote (Canada/US)")]
    [InlineData("Remote - NORAM")]
    public void IsSpainCompatible_RemotoRegionalQueExcluyeEspana_Rechaza(string location)
    {
        // Regresión: los remotos acotados a una región sin España (LATAM,
        // Americas, APAC...) pasaban porque ningún marcador nombraba la
        // región. Los de EEUU/Canadá ya se rechazaban; se fijan aquí para que
        // los nuevos marcadores no los rompan.
        LocationFilter.IsSpainCompatible(location).Should().BeFalse();
    }

    [Theory]
    [InlineData("Remote - EMEA")]
    [InlineData("Remote (EMEA)")]
    [InlineData("Europe")]
    [InlineData("Remote - Europe")]
    [InlineData("Remote - Spain/EMEA")]
    [InlineData("Remote - Spain/LATAM")]
    [InlineData("Amer, Girona")]
    public void IsSpainCompatible_RegionConEspanaOUbicacionEspanola_Acepta(string location)
    {
        // EMEA y Europa incluyen España, y nombrar España gana a cualquier
        // región ajena del mismo texto. "Amer" es un municipio de Girona: por
        // eso la sigla AMER no es marcador.
        LocationFilter.IsSpainCompatible(location).Should().BeTrue();
    }
}
