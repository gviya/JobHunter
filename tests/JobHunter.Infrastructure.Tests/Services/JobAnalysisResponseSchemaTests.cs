using System.Reflection;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using AwesomeAssertions;
using JobHunter.Domain.Entities;
using JobHunter.Infrastructure.Services;

namespace JobHunter.Infrastructure.Tests.Services;

/// <summary>
/// El schema compartido es el contrato que Claude (structured outputs) y
/// Ollama ("format") imponen al modelo: si incumple las reglas de la API,
/// la petición entera falla con un 400; si diverge del parser, se pierden
/// campos en silencio.
/// </summary>
public sealed class JobAnalysisResponseSchemaTests
{
    // Palabras clave que structured outputs NO admite (la API respondería 400).
    private static readonly string[] UnsupportedKeywords =
    [
        "minimum", "maximum", "exclusiveMinimum", "exclusiveMaximum", "multipleOf",
        "minLength", "maxLength", "pattern", "minItems", "maxItems", "uniqueItems",
        "minProperties", "maxProperties",
    ];

    private static JsonObject Schema => JsonNode.Parse(JobAnalysisPrompt.ResponseSchema.GetRawText())!.AsObject();

    private static JsonObject Property(string name) => Schema["properties"]![name]!.AsObject();

    private static string[] Strings(JsonNode? array) =>
        [.. array!.AsArray().Select(n => n!.GetValue<string>())];

    [Fact]
    public void ResponseSchema_TodosLosObjetos_TienenAdditionalPropertiesFalseYTodoRequerido()
    {
        var objects = Descendants(Schema)
            .OfType<JsonObject>()
            .Where(o => o["type"] is JsonValue type && type.GetValue<string>() == "object")
            .ToList();

        objects.Should().NotBeEmpty();
        foreach (var obj in objects)
        {
            obj["additionalProperties"]!.GetValue<bool>().Should().BeFalse();

            var properties = obj["properties"]!.AsObject().Select(p => p.Key);
            Strings(obj["required"]).Should().BeEquivalentTo(properties);
        }
    }

    [Fact]
    public void ResponseSchema_NoUsaPalabrasClaveNoSoportadas()
    {
        var keys = Descendants(Schema)
            .OfType<JsonObject>()
            .SelectMany(o => o.Select(p => p.Key))
            .ToHashSet();

        keys.Should().NotIntersectWith(UnsupportedKeywords);
    }

    [Fact]
    public void ResponseSchema_Propiedades_CoincidenConLosCamposQueLeeElParser()
    {
        // AnalysisJson es privado: se lee por reflexión para que añadir un
        // campo al parser sin añadirlo al schema (o al revés) rompa aquí.
        var parserFields = typeof(JobAnalysisPrompt)
            .GetNestedType("AnalysisJson", BindingFlags.NonPublic)!
            .GetProperties()
            .Select(p => p.GetCustomAttribute<JsonPropertyNameAttribute>()!.Name);

        var schemaFields = Schema["properties"]!.AsObject().Select(p => p.Key);

        schemaFields.Should().BeEquivalentTo(parserFields);
    }

    [Fact]
    public void ResponseSchema_CamposConTipoEspecial_UsanEnteroAnulableYEnums()
    {
        Property("compatibility")["type"]!.GetValue<string>().Should().Be("integer");

        var clientFinalTypes = Property("clientFinal")["anyOf"]!.AsArray()
            .Select(n => n!["type"]!.GetValue<string>());
        clientFinalTypes.Should().BeEquivalentTo("boolean", "null");

        Strings(Property("workMode")["enum"]).Should().Equal("Remote", "Hybrid", "OnSite", "Unknown");
        Strings(Property("seniority")["enum"]).Should().Equal("Junior", "Mid", "Senior", "Lead");
    }

    [Fact]
    public void ResponseSchema_EnumDeWorkMode_CubreTodosLosValoresDelDominio()
    {
        Strings(Property("workMode")["enum"]).Should().BeEquivalentTo(Enum.GetNames<WorkMode>());
    }

    [Fact]
    public void Build_EjemploDelPrompt_UsaLosMismosValoresQueElSchema()
    {
        var offer = new JobOffer
        {
            ExternalId = "1",
            SourceProvider = "Greenhouse",
            Title = "Backend .NET Developer",
            CompanyName = "Acme",
            Url = "https://example.com/1",
        };

        var prompt = JobAnalysisPrompt.Build(offer, UserProfile.CreateDefault());

        prompt.Should().Contain($"\"seniority\": \"{string.Join('|', Strings(Property("seniority")["enum"]))}\"");
        prompt.Should().Contain($"\"workMode\": \"{string.Join('|', Strings(Property("workMode")["enum"]))}\"");
    }

    [Fact]
    public void TryParse_JsonLimpioConformeAlSchema_LeeTodosLosCampos()
    {
        // Lo que devuelve structured outputs: JSON sin fences ni texto extra,
        // con todos los campos presentes (clientFinal a null incluido).
        const string json = """{"summary":"Backend .NET.","seniority":"Senior","importantTechs":["C#"],"requirements":["5 años"],"compatibility":140,"pros":["Remoto"],"cons":["Guardias"],"recommendation":"Aplicar","clientFinal":null,"workMode":"Remote"}""";

        JobAnalysisPrompt.TryParse(json, out var result).Should().BeTrue();

        result.Seniority.Should().Be("Senior");
        result.ImportantTechs.Should().Equal("C#");
        result.Requirements.Should().Equal("5 años");
        result.Compatibility.Should().Be(100); // el schema no puede acotar el rango: lo hace el Clamp
        result.Cons.Should().Equal("Guardias");
        result.IsClientFinal.Should().BeNull();
        result.WorkMode.Should().Be(WorkMode.Remote);
    }

    private static IEnumerable<JsonNode> Descendants(JsonNode node)
    {
        yield return node;

        IEnumerable<JsonNode?> children = node switch
        {
            JsonObject obj => obj.Select(p => p.Value),
            JsonArray array => array,
            _ => [],
        };

        foreach (var child in children.OfType<JsonNode>())
        {
            foreach (var descendant in Descendants(child))
            {
                yield return descendant;
            }
        }
    }
}
