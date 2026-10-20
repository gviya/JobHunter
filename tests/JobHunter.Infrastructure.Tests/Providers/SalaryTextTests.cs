using AwesomeAssertions;
using JobHunter.Infrastructure.Providers;

namespace JobHunter.Infrastructure.Tests.Providers;

public sealed class SalaryTextTests
{
    [Theory]
    // Separador de miles "." (español) y "," (inglés), con y sin espacios.
    [InlineData("30.000 € - 40.000 €", 30000, 40000)]
    [InlineData("30.000€-40.000€", 30000, 40000)]
    [InlineData("30,000 - 40,000 €", 30000, 40000)]
    [InlineData("€30,000 - €40,000", 30000, 40000)]
    [InlineData("30000-40000 EUR", 30000, 40000)]
    [InlineData("30.000 EUR - 40.000 EUR", 30000, 40000)]
    // Sufijo "k"/"mil", también compartido ("30-40k").
    [InlineData("30k - 40k €", 30000, 40000)]
    [InlineData("30K-40K EUR", 30000, 40000)]
    [InlineData("30-40k €", 30000, 40000)]
    [InlineData("30 k€ - 40 k€", 30000, 40000)]
    [InlineData("30 mil - 40 mil euros", 30000, 40000)]
    // Conectores de rango en español e inglés, guiones tipográficos.
    [InlineData("30.000 € \u2013 40.000 €", 30000, 40000)]
    [InlineData("30.000 € \u2014 40.000 €", 30000, 40000)]
    [InlineData("De 30.000 a 40.000 € brutos anuales", 30000, 40000)]
    [InlineData("Entre 30.000 y 40.000 €", 30000, 40000)]
    [InlineData("Desde 30.000 hasta 40.000 €", 30000, 40000)]
    [InlineData("30.000 € brutos - 40.000 € brutos/año", 30000, 40000)]
    [InlineData("From €30,000 to €40,000 per year", 30000, 40000)]
    [InlineData("30.000 € - 40.000 € en 14 pagas", 30000, 40000)]
    [InlineData("30.000 a 40.000 € b/a", 30000, 40000)]
    [InlineData("30.000/40.000 €", 30000, 40000)]
    [InlineData("30/40k €", 30000, 40000)]
    [InlineData("Retribución: 30.000 - 40.000 € brutos anuales + beneficios", 30000, 40000)]
    public void Parse_Horquilla_DevuelveMinimoYMaximo(string text, int expectedMin, int expectedMax)
    {
        SalaryText.Parse(text).Should().Be(new SalaryRange(expectedMin, expectedMax));
    }

    [Theory]
    [InlineData("45.000,50 € - 50.000,75 €", 45000.50, 50000.75)] // decimales con "," (español)
    [InlineData("45,000.50 - 50,000.75 EUR", 45000.50, 50000.75)] // decimales con "." (inglés)
    [InlineData("45.000,00 €", 45000, 45000)]
    [InlineData("45000.00 EUR", 45000, 45000)]
    [InlineData("35,5k - 40,5k €", 35500, 40500)]
    [InlineData("35.5k - 40.5k €", 35500, 40500)]
    public void Parse_ConDecimales_LosRespeta(string text, double expectedMin, double expectedMax)
    {
        SalaryText.Parse(text).Should().Be(new SalaryRange((decimal)expectedMin, (decimal)expectedMax));
    }

    [Theory]
    [InlineData("30\u00A0000 € - 40\u00A0000 €")]                 // &nbsp; como separador de miles
    [InlineData("30\u202F000\u00A0€ - 40\u202F000\u00A0€")]       // espacio fino (formato francés)
    [InlineData("30.000\u00A0€\u00A0-\u00A040.000\u00A0€")]       // &nbsp; alrededor de "€" y del guion
    [InlineData("30 000 € - 40 000 €")]                           // espacio normal como separador
    [InlineData("  30.000 €\t-\n40.000 €  ")]
    public void Parse_ConEspaciosUnicode_LosTrataComoEspacios(string text)
    {
        SalaryText.Parse(text).Should().Be(new SalaryRange(30000, 40000));
    }

    [Theory]
    [InlineData("Hasta 45.000 €")]
    [InlineData("hasta 45.000€ brutos/año")]
    [InlineData("Up to €45k")]
    [InlineData("Máximo 45.000 €")]
    [InlineData("Menos de 45.000 €")]
    public void Parse_SoloTecho_DevuelveSoloMaximo(string text)
    {
        SalaryText.Parse(text).Should().Be(new SalaryRange(null, 45000));
    }

    [Theory]
    [InlineData("Desde 35.000€")]
    [InlineData("desde 35.000 € al año")]
    [InlineData("A partir de 35.000 €")]
    [InlineData("From €35,000")]
    [InlineData("35k+ €")]
    [InlineData("35.000 €+")]
    [InlineData("Más de 35.000 €")]
    [InlineData("Over €35k")]
    public void Parse_SoloSuelo_DevuelveSoloMinimo(string text)
    {
        SalaryText.Parse(text).Should().Be(new SalaryRange(35000, null));
    }

    [Theory]
    [InlineData("35.000 € al año")]
    [InlineData("35.000 €/año")]
    [InlineData("35.000 € brutos/año")]
    [InlineData("35.000 € brutos anuales")]
    [InlineData("35.000 €")]
    [InlineData("35000 EUR")]
    [InlineData("€35,000 per year")]
    [InlineData("35k € annual")]
    [InlineData("35.000 € en 14 pagas")]
    [InlineData("Salario: 35.000 €")]
    public void Parse_ImporteAnualSuelto_EsSueloYTecho(string text)
    {
        SalaryText.Parse(text).Should().Be(new SalaryRange(35000, 35000));
    }

    [Theory]
    [InlineData("2.500 €/mes")]
    [InlineData("2.500 € al mes")]
    [InlineData("3.000 € brutos mensuales")]
    [InlineData("2.000 € - 2.500 € mensuales")]
    [InlineData("€4k/month")]
    [InlineData("€4k/mo")]
    [InlineData("15 €/hora")]
    [InlineData("15 € por hora")]
    [InlineData("€40/h")]
    [InlineData("20 €/hour")]
    [InlineData("150 €/día")]
    [InlineData("150 € al dia")]
    [InlineData("€500 per day")]
    [InlineData("800 €/semana")]
    public void Parse_ImporteNoAnual_DevuelveNull(string text)
    {
        // Pasarlo a anual exigiría suponer pagas y jornada: mejor sin salario.
        SalaryText.Parse(text).Should().BeNull();
    }

    [Theory]
    [InlineData("$80,000 - $100,000")]
    [InlineData("£40,000")]
    [InlineData("60.000 USD")]
    [InlineData("40.000 GBP al año")]
    [InlineData("50.000 dólares")]
    public void Parse_OtraMoneda_DevuelveNull(string text)
    {
        // El perfil está en EUR: comparar dólares o libras con él sería adivinar.
        SalaryText.Parse(text).Should().BeNull();
    }

    [Theory]
    [InlineData("2.500 €")]              // sin periodo: casi seguro mensual
    [InlineData("Hasta 2.000 €")]
    [InlineData("1.200.000 €")]
    [InlineData("35.000k €")]
    [InlineData("30.000 - 40")]
    public void Parse_ImporteFueraDeRangoAnualPlausible_DevuelveNull(string text)
    {
        SalaryText.Parse(text).Should().BeNull();
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("Salario competitivo")]
    [InlineData("A convenir")]
    [InlineData("Según valía")]
    [InlineData("35.000 € + 5.000 € de bonus")]   // dos importes sin conector de rango
    [InlineData("35.000 € 40.000 €")]
    [InlineData("30.000 - 40.000 - 50.000 €")]    // más de dos importes
    [InlineData("40.000 € - 30.000 €")]           // horquilla invertida
    [InlineData("30.000.50 €")]                   // mismo separador para miles y decimales
    [InlineData("30.000,500 €")]                  // ni miles ni decimales válidos
    [InlineData("\u0663\u0665\u0660\u0660\u0660 €")] // "35000" en dígitos arábigo-índicos
    [InlineData("99999999999999999999999999999999 €")]
    public void Parse_TextoAmbiguoOIlegible_DevuelveNull(string? text)
    {
        SalaryText.Parse(text).Should().BeNull();
    }
}
