using System.Net;
using System.Text.Json;
using System.Text.Json.Nodes;
using AwesomeAssertions;
using JobHunter.Domain.Entities;
using JobHunter.Infrastructure.Configuration;
using JobHunter.Infrastructure.Services;
using JobHunter.Infrastructure.Tests.Testing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Moq;

namespace JobHunter.Infrastructure.Tests.Services;

public sealed class ClaudeAnalysisServiceTests
{
    private const string AnalysisJson = """{"summary":"Backend .NET en producto.","seniority":"Senior","importantTechs":["C#",".NET"],"requirements":["5 años con C#"],"compatibility":85,"pros":["Remoto"],"cons":[],"recommendation":"Aplicar","clientFinal":true,"workMode":"Hybrid"}""";

    private readonly Mock<ILogger<ClaudeAnalysisService>> _logger = new();

    private static JobOffer Offer() => new()
    {
        ExternalId = "1",
        SourceProvider = "Greenhouse",
        Title = "Backend .NET Developer",
        CompanyName = "Acme",
        Url = "https://example.com/1",
        Description = "Buscamos backend con C# y .NET.",
    };

    /// <summary>Respuesta de la Messages API con el texto y stop_reason dados.</summary>
    private static string MessageResponse(string text, string stopReason = "end_turn") =>
        new JsonObject
        {
            ["id"] = "msg_1",
            ["type"] = "message",
            ["role"] = "assistant",
            ["content"] = new JsonArray(new JsonObject { ["type"] = "text", ["text"] = text }),
            ["stop_reason"] = stopReason,
        }.ToJsonString();

    private ClaudeAnalysisService CreateService(CapturingHttpMessageHandler handler, Action<AnthropicOptions>? configure = null)
    {
        var options = new AnthropicOptions { ApiKey = "sk-ant-test" };
        configure?.Invoke(options);

        var http = new HttpClient(handler) { BaseAddress = new Uri("https://api.anthropic.com/") };
        return new ClaudeAnalysisService(http, Microsoft.Extensions.Options.Options.Create(options), _logger.Object);
    }

    private void VerifyWarning(string fragment, Times times) =>
        _logger.Verify(
            l => l.Log(
                LogLevel.Warning,
                It.IsAny<EventId>(),
                It.Is<It.IsAnyType>((state, _) => state.ToString()!.Contains(fragment)),
                It.IsAny<Exception?>(),
                It.IsAny<Func<It.IsAnyType, Exception?, string>>()),
            times);

    [Fact]
    public async Task AnalyzeAsync_ConfiguracionPorDefecto_EnviaHaiku55ConEffortYSchema()
    {
        var handler = new CapturingHttpMessageHandler(MessageResponse(AnalysisJson));

        await CreateService(handler).AnalyzeAsync(Offer(), UserProfile.CreateDefault());

        handler.Requests.Should().ContainSingle();
        var request = handler.Requests[0];
        request.Method.Should().Be(HttpMethod.Post);
        request.Uri.Should().Be(new Uri("https://api.anthropic.com/v1/messages"));

        var body = request.Json;
        body.Select(p => p.Key).Should().BeEquivalentTo("model", "max_tokens", "messages", "output_config");
        body["model"]!.GetValue<string>().Should().Be("claude-haiku-5-5");
        body["max_tokens"]!.GetValue<int>().Should().Be(4096);

        var outputConfig = body["output_config"]!.AsObject();
        outputConfig.Select(p => p.Key).Should().BeEquivalentTo("effort", "format");
        outputConfig["effort"]!.GetValue<string>().Should().Be("low");
        outputConfig["format"]!["type"]!.GetValue<string>().Should().Be("json_schema");
        JsonNode.DeepEquals(
                outputConfig["format"]!["schema"],
                JsonNode.Parse(JobAnalysisPrompt.ResponseSchema.GetRawText()))
            .Should().BeTrue("Claude debe recibir exactamente el schema compartido con Ollama");
    }

    [Fact]
    public async Task AnalyzeAsync_Peticion_NoEnviaParametrosDeMuestreoNiPrefill()
    {
        var handler = new CapturingHttpMessageHandler(MessageResponse(AnalysisJson));

        await CreateService(handler).AnalyzeAsync(Offer(), UserProfile.CreateDefault());

        var body = handler.Requests[0].Json;
        // Haiku 5.5 responde 400 a temperature/top_p/top_k; tampoco hay
        // "fallbacks" para Haiku.
        body.Select(p => p.Key).Should().NotContain(["temperature", "top_p", "top_k", "fallbacks"]);

        // Un único mensaje de usuario: nada de prefill del assistant.
        var messages = body["messages"]!.AsArray();
        messages.Count.Should().Be(1);
        messages[0]!["role"]!.GetValue<string>().Should().Be("user");
        messages[0]!["content"]!.GetValue<string>().Should().Contain("<<<OFERTA>>>");
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public async Task AnalyzeAsync_EffortVacio_NoEnviaEffortPeroSiElSchema(string effort)
    {
        // Configuración para volver a Haiku 4.5, que rechaza "effort" con un 400.
        var handler = new CapturingHttpMessageHandler(MessageResponse(AnalysisJson));
        var service = CreateService(handler, o =>
        {
            o.Model = "claude-haiku-4-5";
            o.Effort = effort;
        });

        await service.AnalyzeAsync(Offer(), UserProfile.CreateDefault());

        var body = handler.Requests[0].Json;
        body["model"]!.GetValue<string>().Should().Be("claude-haiku-4-5");
        var outputConfig = body["output_config"]!.AsObject();
        outputConfig.Select(p => p.Key).Should().NotContain("effort");
        outputConfig["format"]!["type"]!.GetValue<string>().Should().Be("json_schema");
    }

    [Fact]
    public async Task AnalyzeAsync_EffortYMaxTokensConfigurados_LosEnviaTalCual()
    {
        var handler = new CapturingHttpMessageHandler(MessageResponse(AnalysisJson));
        var service = CreateService(handler, o =>
        {
            o.Effort = " medium ";
            o.MaxTokens = 8000;
        });

        await service.AnalyzeAsync(Offer(), UserProfile.CreateDefault());

        var body = handler.Requests[0].Json;
        body["max_tokens"]!.GetValue<int>().Should().Be(8000);
        body["output_config"]!["effort"]!.GetValue<string>().Should().Be("medium");
    }

    [Fact]
    public void AnthropicOptions_EffortVacioEnConfiguracion_SobrescribeElLowPorDefecto()
    {
        // Lo que documenta appsettings.json para Haiku 4.5: "Effort": "".
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Anthropic:Model"] = "claude-haiku-4-5",
                ["Anthropic:Effort"] = "",
            })
            .Build();

        var options = configuration.GetSection(AnthropicOptions.SectionName).Get<AnthropicOptions>()!;

        options.Effort.Should().BeEmpty();
        options.MaxTokens.Should().Be(4096);
    }

    [Fact]
    public async Task AnalyzeAsync_BloqueThinkingAntesDelTexto_LeeElBloqueDeTexto()
    {
        // Haiku 5.5 razona por defecto: el primer bloque no es el de texto.
        var response = new JsonObject
        {
            ["content"] = new JsonArray(
                new JsonObject { ["type"] = "thinking", ["thinking"] = "La oferta pide C#...", ["signature"] = "sig" },
                new JsonObject { ["type"] = "text", ["text"] = AnalysisJson }),
            ["stop_reason"] = "end_turn",
        }.ToJsonString();
        var handler = new CapturingHttpMessageHandler(response);

        var result = await CreateService(handler).AnalyzeAsync(Offer(), UserProfile.CreateDefault());

        result.Summary.Should().Be("Backend .NET en producto.");
        result.Seniority.Should().Be("Senior");
        result.Compatibility.Should().Be(85);
        result.IsClientFinal.Should().BeTrue();
        result.WorkMode.Should().Be(WorkMode.Hybrid);
        handler.Requests.Should().ContainSingle();
    }

    [Fact]
    public async Task AnalyzeAsync_StopReasonRefusal_LanzaConLaCategoriaSinReintentar()
    {
        var response = new JsonObject
        {
            ["content"] = new JsonArray(),
            ["stop_reason"] = "refusal",
            ["stop_details"] = new JsonObject { ["type"] = "refusal", ["category"] = "cyber" },
        }.ToJsonString();
        var handler = new CapturingHttpMessageHandler(response);

        var act = () => CreateService(handler).AnalyzeAsync(Offer(), UserProfile.CreateDefault());

        var error = await act.Should().ThrowAsync<InvalidOperationException>();
        error.Which.Message.Should().Contain("refusal").And.Contain("cyber").And.Contain("No se reintenta");
        handler.Requests.Should().ContainSingle("repetir el mismo prompt solo gastaría otra llamada");
    }

    [Theory]
    [InlineData("null")]
    [InlineData("{}")]
    [InlineData("{\"category\":42}")]
    public async Task AnalyzeAsync_RefusalSinCategoriaLegible_LanzaIgualSinReintentar(string stopDetails)
    {
        var response = $$"""{"content":[{"type":"text","text":"No puedo ayudar con eso."}],"stop_reason":"refusal","stop_details":{{stopDetails}}}""";
        var handler = new CapturingHttpMessageHandler(response);

        var act = () => CreateService(handler).AnalyzeAsync(Offer(), UserProfile.CreateDefault());

        (await act.Should().ThrowAsync<InvalidOperationException>())
            .Which.Message.Should().Contain("sin especificar");
        handler.Requests.Should().ContainSingle();
    }

    [Fact]
    public async Task AnalyzeAsync_StopReasonMaxTokensYLuegoCompleta_AvisaYReintenta()
    {
        var handler = new CapturingHttpMessageHandler(
            MessageResponse("""{"summary":"Backend .NET en prod""", stopReason: "max_tokens"),
            MessageResponse(AnalysisJson));

        var result = await CreateService(handler).AnalyzeAsync(Offer(), UserProfile.CreateDefault());

        result.Compatibility.Should().Be(85);
        handler.Requests.Should().HaveCount(2);
        VerifyWarning("max_tokens", Times.Once());
    }

    [Fact]
    public async Task AnalyzeAsync_StopReasonMaxTokensEnAmbosIntentos_AvisaCadaVezYFallaAlParsear()
    {
        // Todo el presupuesto se fue en razonar: no llega bloque de texto.
        var truncated = new JsonObject
        {
            ["content"] = new JsonArray(new JsonObject { ["type"] = "thinking", ["thinking"] = "...", ["signature"] = "sig" }),
            ["stop_reason"] = "max_tokens",
        }.ToJsonString();
        var handler = new CapturingHttpMessageHandler(truncated);

        var act = () => CreateService(handler).AnalyzeAsync(Offer(), UserProfile.CreateDefault());

        (await act.Should().ThrowAsync<InvalidOperationException>())
            .Which.Message.Should().Contain("No se pudo parsear");
        handler.Requests.Should().HaveCount(2);
        VerifyWarning("max_tokens", Times.Exactly(2));
    }

    [Fact]
    public async Task AnalyzeAsync_RespuestaCompleta_NoAvisaDeTruncado()
    {
        var handler = new CapturingHttpMessageHandler(MessageResponse(AnalysisJson));

        await CreateService(handler).AnalyzeAsync(Offer(), UserProfile.CreateDefault());

        VerifyWarning("max_tokens", Times.Never());
    }

    [Fact]
    public async Task AnalyzeAsync_ErrorHttp_IncluyeElBodyDeLaApiEnLaExcepcion()
    {
        var handler = new CapturingHttpMessageHandler(
            """{"type":"error","error":{"type":"invalid_request_error","message":"effort: not supported by this model"}}""")
        {
            Status = HttpStatusCode.BadRequest,
        };

        var act = () => CreateService(handler).AnalyzeAsync(Offer(), UserProfile.CreateDefault());

        (await act.Should().ThrowAsync<HttpRequestException>())
            .Which.Message.Should().Contain("400").And.Contain("effort: not supported by this model");
        handler.Requests.Should().ContainSingle();
    }

    [Fact]
    public async Task AnalyzeAsync_SinApiKey_LanzaSinLlamarALaApi()
    {
        var handler = new CapturingHttpMessageHandler(MessageResponse(AnalysisJson));
        var service = CreateService(handler, o => o.ApiKey = "");

        var act = () => service.AnalyzeAsync(Offer(), UserProfile.CreateDefault());

        await act.Should().ThrowAsync<InvalidOperationException>().WithMessage("*ApiKey*");
        handler.Requests.Should().BeEmpty();
    }

    [Fact]
    public async Task AnalyzeAsync_PeticionesSucesivas_ReutilizanElMismoSchema()
    {
        // El schema es un JsonElement estático compartido: serializarlo en
        // varias peticiones no debe alterarlo ni fallar.
        var handler = new CapturingHttpMessageHandler(MessageResponse(AnalysisJson));
        var service = CreateService(handler);

        await service.AnalyzeAsync(Offer(), UserProfile.CreateDefault());
        await service.AnalyzeAsync(Offer(), UserProfile.CreateDefault());

        var first = handler.Requests[0].Json["output_config"]!["format"]!["schema"];
        var second = handler.Requests[1].Json["output_config"]!["format"]!["schema"];
        JsonNode.DeepEquals(first, second).Should().BeTrue();
        JobAnalysisPrompt.ResponseSchema.ValueKind.Should().Be(JsonValueKind.Object);
    }
}
