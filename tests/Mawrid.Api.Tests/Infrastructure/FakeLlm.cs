using System.Net;
using System.Net.Http.Json;
using System.Text.Json;

namespace Mawrid.Api.Tests.Infrastructure;

/// <summary>
/// Stands in for the AI provider's chat completions API. Tests set <see cref="Answer"/> (the JSON
/// the "model" returns) or <see cref="Status"/> to simulate an outage, and read <see cref="LastRequest"/>.
/// </summary>
public sealed class FakeLlm
{
    public string Answer { get; set; } = "{}";

    public HttpStatusCode Status { get; set; } = HttpStatusCode.OK;

    public JsonDocument? LastRequest { get; private set; }

    public int Calls { get; private set; }

    public HttpMessageHandler CreateHandler() => new Handler(this);

    private sealed class Handler(FakeLlm llm) : HttpMessageHandler
    {
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            llm.Calls++;
            llm.LastRequest = JsonDocument.Parse(await request.Content!.ReadAsStringAsync(cancellationToken));
            if (llm.Status != HttpStatusCode.OK)
            {
                return new HttpResponseMessage(llm.Status);
            }

            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = JsonContent.Create(new
                {
                    model = "fake-model",
                    choices = new[] { new { index = 0, message = new { role = "assistant", content = llm.Answer } } },
                    usage = new { total_tokens = 123 },
                }),
            };
        }
    }
}
