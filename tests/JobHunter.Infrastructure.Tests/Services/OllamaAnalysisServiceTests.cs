using System.Net;
using System.Text.Json.Nodes;
using AwesomeAssertions;
using JobHunter.Domain.Entities;
using JobHunter.Infrastructure.Configuration;
using JobHunter.Infrastructure.Services;
using JobHunter.Infrastructure.Tests.Testing;
using Microsoft.Extensions.Logging;
using Moq;

namespace JobHunter.Infrastructure.Tests.Services;

public sealed class OllamaAnalysisServiceTests
{
    private const string AnalysisJson = """{"summary":"Backend .NET.","seniority":"Mid","importantTechs":["C#"],"requirements":[],"compatibility":70,"pros":[],"cons":[],"recommendation":"Aplicar","clientFinal":false,"workMode":"Remote"}""";

    private readonly Mock<ILogger<OllamaAnalysisService>> _logger = new();

    private static JobOffer Offer() => new()
    {
        ExternalId = "1",
        SourceProvider = "Greenhouse",
        Title = "Backend .NET Developer",
        CompanyName = "Acme",
        Url = "https://example.com/1",
        Description = "Buscamos backend con C# y .NET.",
    };

    private static string GenerateResponse(string response, string doneReason = "stop") =>
        new JsonObject { ["model"] = "qwen3.6:27b", ["response"] = response, ["done"] = true, ["done_reason"] = doneReason }
            .ToJsonString();

    private OllamaAnalysisService CreateService(CapturingHttpMessageHandler handler)
    {
        var http = new HttpClient(handler) { BaseAddress = new Uri("http://localhost:11434/") };
        return new OllamaAnalysisService(http, Microsoft.Extensions.Options.Options.Create(new OllamaOptions()), _logger.Object);
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
    public async Task AnalyzeAsync_Peticion_EnviaElSchemaCompartidoEnFormatSinThinkNiStream()
    {
        var handler = new CapturingHttpMessageHandler(GenerateResponse(AnalysisJson));

        await CreateService(handler).AnalyzeAsync(Offer(), UserProfile.CreateDefault());

        handler.Requests.Should().ContainSingle();
        var request = handler.Requests[0];
        request.Uri.Should().Be(new Uri("http://localhost:11434/api/generate"));

        var body = request.Json;
        body.Select(p => p.Key).Should().BeEquivalentTo("model", "prompt", "format", "stream", "think");
        body["model"]!.GetValue<string>().Should().Be(new OllamaOptions().AnalysisModel);
        body["prompt"]!.GetValue<string>().Should().Contain("<<<OFERTA>>>");
        body["stream"]!.GetValue<bool>().Should().BeFalse();
        body["think"]!.GetValue<bool>().Should().BeFalse();
        JsonNode.DeepEquals(body["format"], JsonNode.Parse(JobAnalysisPrompt.ResponseSchema.GetRawText()))
            .Should().BeTrue("Ollama debe imponer el mismo contrato que Claude");
    }

    [Fact]
    public async Task AnalyzeAsync_RespuestaValida_DevuelveElAnalisis()
    {
        var handler = new CapturingHttpMessageHandler(GenerateResponse(AnalysisJson));

        var result = await CreateService(handler).AnalyzeAsync(Offer(), UserProfile.CreateDefault());

        result.Compatibility.Should().Be(70);
        result.IsClientFinal.Should().BeFalse();
        result.WorkMode.Should().Be(WorkMode.Remote);
        VerifyWarning("longitud", Times.Never());
    }

    [Fact]
    public async Task AnalyzeAsync_DoneReasonLength_AvisaDeTruncadoYReintenta()
    {
        var handler = new CapturingHttpMessageHandler(
            GenerateResponse("""{"summary":"Backend""", doneReason: "length"),
            GenerateResponse(AnalysisJson));

        var result = await CreateService(handler).AnalyzeAsync(Offer(), UserProfile.CreateDefault());

        result.Compatibility.Should().Be(70);
        handler.Requests.Should().HaveCount(2);
        VerifyWarning("longitud", Times.Once());
    }

    [Fact]
    public async Task AnalyzeAsync_ErrorHttp_IncluyeElBodyDeOllamaEnLaExcepcion()
    {
        var handler = new CapturingHttpMessageHandler("""{"error":"model 'qwen3.6:27b' not found"}""")
        {
            Status = HttpStatusCode.NotFound,
        };

        var act = () => CreateService(handler).AnalyzeAsync(Offer(), UserProfile.CreateDefault());

        (await act.Should().ThrowAsync<HttpRequestException>())
            .Which.Message.Should().Contain("404").And.Contain("not found");
        handler.Requests.Should().ContainSingle();
    }
}
