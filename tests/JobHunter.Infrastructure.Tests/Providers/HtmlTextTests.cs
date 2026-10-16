using AwesomeAssertions;
using JobHunter.Infrastructure.Providers;

namespace JobHunter.Infrastructure.Tests.Providers;

public sealed class HtmlTextTests
{
    // "content" tal y como lo devuelve Greenhouse con content=true: el HTML
    // entero viene ESCAPADO, y lo que ya era una entidad en el original
    // (&nbsp;, &amp;, el "<T>" de un genérico) llega doblemente escapado.
    private const string GreenhouseEscapedContent =
        "&lt;div class=&quot;content-intro&quot;&gt;&lt;p&gt;&lt;strong&gt;Sobre nosotros&lt;/strong&gt;&lt;/p&gt;" +
        "&lt;p&gt;Somos una empresa de producto&amp;nbsp;SaaS en Madrid.&lt;/p&gt;&lt;/div&gt;" +
        "&lt;h3&gt;Requisitos&lt;/h3&gt;" +
        "&lt;ul&gt;&lt;li&gt;5+ años con C# y .NET&lt;/li&gt;" +
        "&lt;li&gt;Experiencia con Task&amp;lt;T&amp;gt; y async/await&lt;/li&gt;" +
        "&lt;li&gt;SQL Server &amp;amp; Docker&lt;/li&gt;&lt;/ul&gt;";

    [Fact]
    public void ToPlainText_ConHtmlEscapadoDeGreenhouse_DevuelveTextoLimpio()
    {
        // Regresión: Greenhouse se guardaba tal cual ("&lt;p&gt;..."), lo que
        // inflaba la longitud que puntúa la calidad de la información y
        // mandaba maquetación al prompt de la IA.
        var text = HtmlText.ToPlainText(GreenhouseEscapedContent);

        text.Should().Be(
            """
            Sobre nosotros
            Somos una empresa de producto SaaS en Madrid.
            Requisitos
            - 5+ años con C# y .NET
            - Experiencia con Task<T> y async/await
            - SQL Server & Docker
            """.ReplaceLineEndings("\n"));
    }

    [Fact]
    public void ToPlainText_ConSnippetDeJooble_QuitaEtiquetasInlineYEntidades()
    {
        var text = HtmlText.ToPlainText("Buscamos <b>desarrollador</b> con&nbsp;experiencia en <strong>.NET</strong>.");

        // Las etiquetas inline dejan un espacio (mejor eso que pegar palabras).
        text.Should().Be("Buscamos desarrollador con experiencia en .NET .");
    }

    [Fact]
    public void ToPlainText_ConSaltosDeLineaDelPropioHtml_LosTrataComoEspacios()
    {
        // En HTML el salto de línea del código fuente no separa nada: solo
        // los límites de bloque (<p>, <br>...) generan líneas nuevas.
        var text = HtmlText.ToPlainText("<p>Backend\n   developer</p>\n<p>en&nbsp;Madrid<br/>remoto</p>");

        text.Should().Be("Backend developer\nen Madrid\nremoto");
    }

    [Fact]
    public void ToPlainText_ConLiQueEnvuelveUnParrafo_UneLaVinetaConSuTexto()
    {
        var text = HtmlText.ToPlainText("<ul><li><p>Inglés B2</p></li><li><p>3 años con EF Core</p></li></ul>");

        text.Should().Be("- Inglés B2\n- 3 años con EF Core");
    }

    [Fact]
    public void ToPlainText_ConScriptStyleYComentarios_LosEliminaConSuContenido()
    {
        var text = HtmlText.ToPlainText(
            "<style>.x{color:red}</style><p>Oferta</p><!-- tracking --><script>alert('x')</script><p>real</p>");

        text.Should().Be("Oferta\nreal");
    }

    [Fact]
    public void ToPlainText_ConSignoMenorEnElTexto_NoSeComeLoQueSigue()
    {
        // Un "<" del texto (ya decodificado) seguido de espacio no es una
        // etiqueta: no debe tragarse todo hasta el siguiente ">".
        var text = HtmlText.ToPlainText("<p>Equipo &lt; 10 personas y <b>remoto</b></p>");

        text.Should().Be("Equipo < 10 personas y remoto");
    }

    [Fact]
    public void ToPlainText_ConTextoPlano_LoDevuelveSinCambios()
    {
        HtmlText.ToPlainText("Backend .NET en remoto").Should().Be("Backend .NET en remoto");
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("<p></p><br/>")]
    [InlineData("&lt;p&gt;&lt;/p&gt;")]
    public void ToPlainText_SinTextoReal_DevuelveNull(string? html)
    {
        HtmlText.ToPlainText(html).Should().BeNull();
    }
}
