using System.Collections.Concurrent;
using System.Net;

namespace Mawrid.Api.Tests.Infrastructure;

/// <summary>Stands in for the ERP: records every webhook call and answers with the status the test chooses.</summary>
public sealed class FakeWebhookReceiver
{
    private readonly ConcurrentQueue<ReceivedWebhook> _received = new();

    public IReadOnlyCollection<ReceivedWebhook> Received => _received.ToArray();

    /// <summary>The status to answer with, per request URL. Default 204.</summary>
    public ConcurrentDictionary<string, HttpStatusCode> StatusByUrl { get; } = new();

    public IEnumerable<ReceivedWebhook> For(string url) => Received.Where(r => r.Url == url);

    internal async Task<HttpResponseMessage> HandleAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var url = request.RequestUri!.ToString();
        var headers = request.Headers.ToDictionary(h => h.Key, h => string.Join(",", h.Value), StringComparer.OrdinalIgnoreCase);
        var body = request.Content is null ? string.Empty : await request.Content.ReadAsStringAsync(cancellationToken);
        _received.Enqueue(new ReceivedWebhook(url, headers, body));
        return new HttpResponseMessage(StatusByUrl.GetValueOrDefault(url, HttpStatusCode.NoContent));
    }

    /// <summary>A new handler each time: HttpClientFactory disposes the handlers it creates.</summary>
    public HttpMessageHandler CreateHandler() => new Handler(this);

    private sealed class Handler(FakeWebhookReceiver receiver) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            receiver.HandleAsync(request, cancellationToken);
    }
}

public sealed record ReceivedWebhook(string Url, IReadOnlyDictionary<string, string> Headers, string Body);
