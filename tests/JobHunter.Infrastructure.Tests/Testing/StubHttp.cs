using System.Net;
using System.Text;

namespace JobHunter.Infrastructure.Tests.Testing;

/// <summary>
/// Handler que responde SIEMPRE con el mismo JSON (para tests). El charset
/// es configurable para poder reproducir respuestas reales no estándar,
/// como el "charset=utf8" (sin guión) de Adzuna.
/// </summary>
internal sealed class StubHttpMessageHandler(string json, HttpStatusCode status = HttpStatusCode.OK, string? charSet = "utf-8") : HttpMessageHandler
{
    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var content = new StringContent(json, Encoding.UTF8, "application/json");
        content.Headers.ContentType!.CharSet = charSet;

        return Task.FromResult(new HttpResponseMessage(status) { Content = content });
    }
}

/// <summary>IHttpClientFactory que entrega clientes con un handler fijo.</summary>
internal sealed class StubHttpClientFactory(HttpMessageHandler handler) : IHttpClientFactory
{
    public HttpClient CreateClient(string name) => new(handler);
}
