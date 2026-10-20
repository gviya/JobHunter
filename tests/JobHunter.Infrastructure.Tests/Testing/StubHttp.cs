using System.Net;
using System.Text;
using System.Text.Json.Nodes;

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

/// <summary>
/// Handler que CAPTURA cada petición (con el body ya leído, para poder
/// comprobar el JSON exacto que se envía) y responde con una secuencia de
/// JSON: la llamada N recibe la respuesta N y, agotada la lista, se repite
/// la última. Así se prueban reintentos (primera mala, segunda buena) y se
/// cuenta cuántas llamadas hubo.
/// </summary>
internal sealed class CapturingHttpMessageHandler(params string[] responses) : HttpMessageHandler
{
    private readonly List<CapturedRequest> _requests = [];

    /// <summary>Estado HTTP de todas las respuestas (OK por defecto).</summary>
    public HttpStatusCode Status { get; init; } = HttpStatusCode.OK;

    public IReadOnlyList<CapturedRequest> Requests => _requests;

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        // Se lee aquí, dentro del envío: después el cliente puede haber
        // liberado ya el contenido de la petición.
        var body = request.Content is null ? null : await request.Content.ReadAsStringAsync(cancellationToken);
        _requests.Add(new CapturedRequest(request.Method, request.RequestUri, body));

        var json = responses.Length == 0 ? "{}" : responses[Math.Min(_requests.Count, responses.Length) - 1];

        return new HttpResponseMessage(Status) { Content = new StringContent(json, Encoding.UTF8, "application/json") };
    }
}

/// <summary>Petición capturada por <see cref="CapturingHttpMessageHandler"/>.</summary>
internal sealed record CapturedRequest(HttpMethod Method, Uri? Uri, string? Body)
{
    /// <summary>Body parseado como árbol JSON para inspeccionar su forma.</summary>
    public JsonObject Json => JsonNode.Parse(Body ?? throw new InvalidOperationException("La petición no tenía body."))!.AsObject();
}
