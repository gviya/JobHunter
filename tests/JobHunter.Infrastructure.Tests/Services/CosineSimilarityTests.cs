using AwesomeAssertions;
using JobHunter.Infrastructure.Configuration;
using JobHunter.Infrastructure.Services;
using Microsoft.Extensions.Options;

namespace JobHunter.Infrastructure.Tests.Services;

public sealed class CosineSimilarityTests
{
    private static OllamaEmbeddingService CreateService()
        => new(new HttpClient(), Options.Create(new OllamaOptions()));

    [Fact]
    public void CosineSimilarity_VectoresIdenticos_EsUno()
    {
        var service = CreateService();

        var similarity = service.CosineSimilarity([1f, 2f, 3f], [1f, 2f, 3f]);

        similarity.Should().BeApproximately(1f, 0.0001f);
    }

    [Fact]
    public void CosineSimilarity_VectoresOrtogonales_EsCero()
    {
        var service = CreateService();

        var similarity = service.CosineSimilarity([1f, 0f], [0f, 1f]);

        similarity.Should().BeApproximately(0f, 0.0001f);
    }
}
