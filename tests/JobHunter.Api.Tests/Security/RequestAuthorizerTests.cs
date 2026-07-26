using System.Net;
using AwesomeAssertions;
using JobHunter.Api.Security;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;

namespace JobHunter.Api.Tests.Security;

/// <summary>
/// Verifica la puerta de seguridad que comparten los endpoints sensibles y el
/// dashboard de Hangfire: local libre, remoto solo con API key válida.
/// </summary>
public sealed class RequestAuthorizerTests
{
    private static HttpContext ContextFrom(string? remoteIp)
    {
        var http = new DefaultHttpContext();
        http.Connection.RemoteIpAddress = remoteIp is null ? null : IPAddress.Parse(remoteIp);
        return http;
    }

    private static IConfiguration ConfigWithKey(string? key) =>
        new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> { ["Security:ApiKey"] = key })
            .Build();

    [Fact]
    public void IsLocal_PeticionLoopback_EsLocal() =>
        RequestAuthorizer.IsLocal(ContextFrom("127.0.0.1")).Should().BeTrue();

    [Fact]
    public void IsLocal_SinIpRemota_EsLocal() =>
        RequestAuthorizer.IsLocal(ContextFrom(null)).Should().BeTrue();

    [Fact]
    public void IsLocal_IpPublicaRemota_NoEsLocal() =>
        RequestAuthorizer.IsLocal(ContextFrom("203.0.113.10")).Should().BeFalse();

    [Fact]
    public void HasValidApiKey_SinClaveConfigurada_Rechaza() =>
        RequestAuthorizer.HasValidApiKey(ConfigWithKey(null), "cualquier-cosa").Should().BeFalse();

    [Fact]
    public void HasValidApiKey_ClaveCorrecta_Acepta() =>
        RequestAuthorizer.HasValidApiKey(ConfigWithKey("s3cr3t"), "s3cr3t").Should().BeTrue();

    [Theory]
    [InlineData("otra")]
    [InlineData("")]
    [InlineData(null)]
    public void HasValidApiKey_ClaveIncorrectaOVacia_Rechaza(string? provided) =>
        RequestAuthorizer.HasValidApiKey(ConfigWithKey("s3cr3t"), provided).Should().BeFalse();
}
