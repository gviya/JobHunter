using System.Net;
using AwesomeAssertions;
using JobHunter.Infrastructure.Configuration;
using JobHunter.Infrastructure.Services;
using JobHunter.Infrastructure.Tests.Testing;
using Microsoft.Extensions.Options;

namespace JobHunter.Infrastructure.Tests.Services;

public sealed class OllamaEmbeddingServiceTests
{
    private static OllamaEmbeddingService CreateService(CapturingHttpMessageHandler handler)
    {
        var http = new HttpClient(handler) { BaseAddress = new Uri("http://localhost:11434/") };
        return new OllamaEmbeddingService(http, Options.Create(new OllamaOptions()));
    }

    [Fact]
    public async Task GetEmbeddingAsync_Peticion_UsaApiEmbedConModelEInput()
    {
        var handler = new CapturingHttpMessageHandler("""{"model":"nomic-embed-text","embeddings":[[0.1,0.2]]}""");

        await CreateService(handler).GetEmbeddingAsync("Backend .NET Acme");

        handler.Requests.Should().ContainSingle();
        var request = handler.Requests[0];
        request.Method.Should().Be(HttpMethod.Post);
        request.Uri.Should().Be(new Uri("http://localhost:11434/api/embed"));

        // El endpoint obsoleto usaba "prompt"; /api/embed espera "input".
        var body = request.Json;
        body.Select(p => p.Key).Should().BeEquivalentTo("model", "input");
        body["model"]!.GetValue<string>().Should().Be("nomic-embed-text");
        body["input"]!.GetValue<string>().Should().Be("Backend .NET Acme");
    }

    [Fact]
    public async Task GetEmbeddingAsync_RespuestaConVectores_DevuelveElPrimero()
    {
        var handler = new CapturingHttpMessageHandler("""{"model":"nomic-embed-text","embeddings":[[0.1,-0.2,0.3]]}""");

        var embedding = await CreateService(handler).GetEmbeddingAsync("texto");

        embedding.Should().Equal(0.1f, -0.2f, 0.3f);
    }

    [Theory]
    [InlineData("""{"embeddings":[]}""")]
    [InlineData("""{"embeddings":null}""")]
    [InlineData("""{"embeddings":[null]}""")]
    [InlineData("{}")]
    // Forma del endpoint obsoleto: ya no se lee.
    [InlineData("""{"embedding":[0.1,0.2]}""")]
    public async Task GetEmbeddingAsync_SinVectores_DevuelveVacio(string json)
    {
        var handler = new CapturingHttpMessageHandler(json);

        var embedding = await CreateService(handler).GetEmbeddingAsync("texto");

        embedding.Should().BeEmpty();
    }

    [Fact]
    public async Task GetEmbeddingAsync_ErrorHttp_IncluyeElBodyDeOllamaEnLaExcepcion()
    {
        var handler = new CapturingHttpMessageHandler("""{"error":"model \"nomic-embed-text\" not found, try pulling it first"}""")
        {
            Status = HttpStatusCode.NotFound,
        };

        var act = () => CreateService(handler).GetEmbeddingAsync("texto");

        (await act.Should().ThrowAsync<HttpRequestException>())
            .Which.Message.Should().Contain("404").And.Contain("try pulling it first");
    }
}
