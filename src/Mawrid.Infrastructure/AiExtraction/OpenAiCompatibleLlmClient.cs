using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Mawrid.Application.Payables.Extraction;
using Mawrid.Domain.Common;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Mawrid.Infrastructure.AiExtraction;

/// <summary>
/// Calls <c>POST {base}/chat/completions</c>, which Pollinations, Ollama and OpenAI all support.
/// Retries, timeouts and the circuit breaker come from the standard resilience handler (see DependencyInjection).
/// </summary>
internal sealed partial class OpenAiCompatibleLlmClient(HttpClient http, IOptions<AiExtractionOptions> options, ILogger<OpenAiCompatibleLlmClient> logger)
    : ILlmClient
{
    public async Task<Result<LlmAnswer>> CompleteJsonAsync(string systemPrompt, string userPrompt, CancellationToken cancellationToken)
    {
        var settings = options.Value;
        using var request = new HttpRequestMessage(HttpMethod.Post, new Uri(settings.ResolvedBaseUrl, "chat/completions"))
        {
            Content = JsonContent.Create(new
            {
                model = settings.ResolvedModel,
                temperature = 0,
                response_format = new { type = "json_object" },
                messages = new[]
                {
                    new { role = "system", content = systemPrompt },
                    new { role = "user", content = userPrompt },
                },
            }),
        };
        if (!string.IsNullOrEmpty(settings.ApiKey))
        {
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", settings.ApiKey);
        }

        var started = TimeProvider.System.GetTimestamp();
        try
        {
            using var response = await http.SendAsync(request, cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                LogFailed(settings.Provider, (int)response.StatusCode);
                return ExtractionErrors.ProviderUnavailable;
            }

            var body = await response.Content.ReadFromJsonAsync<ChatCompletion>(cancellationToken);
            var content = body?.Choices?.FirstOrDefault()?.Message?.Content;
            LogCompleted(settings.Provider, body?.Model ?? settings.ResolvedModel, body?.Usage?.TotalTokens, TimeProvider.System.GetElapsedTime(started).TotalMilliseconds);
            return string.IsNullOrWhiteSpace(content)
                ? ExtractionErrors.UnreadableAnswer
                : new LlmAnswer(content, settings.Provider.ToString(), body?.Model ?? settings.ResolvedModel);
        }
        catch (Exception exception) when (exception is HttpRequestException or JsonException
            or Polly.CircuitBreaker.BrokenCircuitException or Polly.Timeout.TimeoutRejectedException
            || (exception is OperationCanceledException && !cancellationToken.IsCancellationRequested))
        {
            LogUnavailable(exception, settings.Provider);
            return ExtractionErrors.ProviderUnavailable;
        }
    }

    // Only metadata is logged, never the invoice text or the answer: they can hold business data.
    [LoggerMessage(Level = LogLevel.Information, Message = "AI extraction by {Provider} model {Model}: {Tokens} tokens in {ElapsedMs:0} ms")]
    private partial void LogCompleted(AiProvider provider, string model, int? tokens, double elapsedMs);

    [LoggerMessage(Level = LogLevel.Warning, Message = "AI provider {Provider} answered HTTP {Status}")]
    private partial void LogFailed(AiProvider provider, int status);

    [LoggerMessage(Level = LogLevel.Warning, Message = "AI provider {Provider} could not be reached")]
    private partial void LogUnavailable(Exception exception, AiProvider provider);

    private sealed record ChatCompletion(string? Model, IReadOnlyList<Choice>? Choices, TokenUsage? Usage);

    private sealed record Choice(ChatMessage? Message);

    private sealed record ChatMessage(string? Content);

    private sealed record TokenUsage([property: System.Text.Json.Serialization.JsonPropertyName("total_tokens")] int? TotalTokens);
}
