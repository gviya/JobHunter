using AwesomeAssertions;
using JobHunter.Application.Features.Pipeline;

namespace JobHunter.Application.Tests.Features.Pipeline;

/// <summary>
/// Reglas de la clave de título del dedup determinista: lo que distintas
/// fuentes escriben distinto para la MISMA oferta debe colapsar a la misma
/// clave, y puestos distintos nunca deben acabar compartiéndola.
/// </summary>
public sealed class NormalizeTitleTests
{
    private static string Normalize(string title) => RunDailyPipeline.Handler.NormalizeTitle(title);

    [Theory]
    [InlineData("Senior Backend Engineer (m/f/d)", "Senior Backend Engineer")]
    [InlineData("Senior Backend Engineer (m/w/d)", "Senior Backend Engineer")]
    [InlineData("Senior Backend Engineer (w/m/d)", "Senior Backend Engineer")]
    [InlineData("Senior Backend Engineer (f/m/x)", "Senior Backend Engineer")]
    [InlineData("Senior Backend Engineer (m/f)", "Senior Backend Engineer")]
    [InlineData("Senior Backend Engineer (M/F/D)", "Senior Backend Engineer")]
    [InlineData("Senior Backend Engineer [m/f/d]", "Senior Backend Engineer")]
    [InlineData("Senior Backend Engineer ( m / w / d )", "Senior Backend Engineer")]
    [InlineData("Senior Backend Engineer (m/f/div)", "Senior Backend Engineer")]
    [InlineData("Senior Backend Engineer m/w/d", "Senior Backend Engineer")]
    [InlineData("Senior Backend Engineer (all genders)", "Senior Backend Engineer")]
    [InlineData("Développeur Backend (H/F)", "Developpeur Backend")]
    [InlineData("Desarrollador Backend (h/m)", "Desarrollador Backend")]
    [InlineData("Desarrollador/a Backend .NET", "Desarrollador Backend .NET")]
    [InlineData("Ingeniero/a de Software", "Ingeniero de Software")]
    [InlineData("Programadores/as C#", "Programadores C#")]
    [InlineData("Desarrollador(a) C#", "Desarrollador C#")]
    public void NormalizeTitle_MarcadorDeGenero_SeIgnora(string withMarker, string withoutMarker)
        => Normalize(withMarker).Should().Be(Normalize(withoutMarker));

    [Theory]
    [InlineData("Ingeniería de Datos", "ingenieria de datos")] // acentos fuera
    [InlineData("  Backend   Developer -- Remote  ", "backend developer remote")] // puntuación y espacios colapsados
    [InlineData("Backend Developer – .NET / Azure", "backend developer net azure")]
    [InlineData("Desarrollador/a Backend .NET (m/f/d)", "desarrollador backend net")]
    [InlineData("C# / C++ Developer", "c# c++ developer")] // '#' y '+' se conservan
    [InlineData("Linux I/O Engineer", "linux i o engineer")] // "/o" tras una sola letra no es género
    [InlineData("", "")]
    public void NormalizeTitle_ProduceLaClaveEsperada(string title, string expected)
        => Normalize(title).Should().Be(expected);

    [Theory]
    [InlineData("Backend Developer (Senior)", "Backend Developer (Junior)")] // el resto del paréntesis cuenta
    [InlineData("C# Developer", "C++ Developer")]
    [InlineData("C# Developer", "C Developer")]
    [InlineData("Backend Developer", "Frontend Developer")]
    [InlineData("Backend Developer", "Backend Developer (Remote)")]
    [InlineData("Ingeniero de Software", "Ingeniera de Software")] // conservador: sin marcador no se adivina
    public void NormalizeTitle_PuestosDistintos_NoComparteClave(string first, string second)
        => Normalize(first).Should().NotBe(Normalize(second));
}
