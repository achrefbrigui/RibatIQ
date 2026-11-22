using System.Net;
using System.Text;

namespace RibatIQ.IntegrationTests;

/// <summary>An HTTP handler that replays scripted responses and records requests, for testing provider adapters offline.</summary>
internal sealed class StubHttpHandler : HttpMessageHandler
{
    private readonly Queue<Func<HttpRequestMessage, HttpResponseMessage>> _responses = new();

    public List<RecordedRequest> Requests { get; } = [];

    public StubHttpHandler Respond(HttpStatusCode status, string json, params (string Name, string Value)[] headers)
    {
        _responses.Enqueue(_ =>
        {
            var response = new HttpResponseMessage(status) { Content = new StringContent(json, Encoding.UTF8, "application/json") };
            foreach (var (name, value) in headers)
            {
                response.Headers.TryAddWithoutValidation(name, value);
            }

            return response;
        });
        return this;
    }

    public StubHttpHandler Throw(Exception exception)
    {
        _responses.Enqueue(_ => throw exception);
        return this;
    }

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var body = request.Content is null ? string.Empty : await request.Content.ReadAsStringAsync(cancellationToken);
        Requests.Add(new RecordedRequest(
            request.Method,
            request.RequestUri!,
            request.Headers.ToDictionary(h => h.Key, h => string.Join(',', h.Value), StringComparer.OrdinalIgnoreCase),
            body));

        if (_responses.Count == 0)
        {
            throw new InvalidOperationException("No scripted response left.");
        }

        return _responses.Dequeue()(request);
    }

    internal sealed record RecordedRequest(HttpMethod Method, Uri Uri, IReadOnlyDictionary<string, string> Headers, string Body);
}
