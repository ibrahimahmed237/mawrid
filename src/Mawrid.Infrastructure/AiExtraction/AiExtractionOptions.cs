namespace Mawrid.Infrastructure.AiExtraction;

/// <summary>Which OpenAI-compatible chat API reads invoices (section "AiExtraction").</summary>
public sealed class AiExtractionOptions
{
    public const string SectionName = "AiExtraction";

    public AiProvider Provider { get; set; } = AiProvider.Pollinations;

    /// <summary>Overrides the provider's default base URL (the part before <c>/chat/completions</c>).</summary>
    public string? BaseUrl { get; set; }

    /// <summary>Overrides the provider's default model.</summary>
    public string? Model { get; set; }

    /// <summary>Only for OpenAI. Keep it in user-secrets or a vault, never in a committed file.</summary>
    public string? ApiKey { get; set; }

    /// <summary>How long one attempt may take. Models are slow; the default is 60 seconds.</summary>
    public TimeSpan AttemptTimeout { get; set; } = TimeSpan.FromSeconds(60);

    public Uri ResolvedBaseUrl => new((BaseUrl ?? Provider switch
    {
        AiProvider.Pollinations => "https://text.pollinations.ai/openai",
        AiProvider.Ollama => "http://localhost:11434/v1",
        AiProvider.OpenAI => "https://api.openai.com/v1",
        _ => throw new InvalidOperationException($"Unknown AI provider {Provider}."),
    }).TrimEnd('/') + "/");

    public string ResolvedModel => Model ?? Provider switch
    {
        AiProvider.Pollinations => "openai",
        AiProvider.Ollama => "qwen2.5:3b",
        AiProvider.OpenAI => "gpt-4o-mini",
        _ => throw new InvalidOperationException($"Unknown AI provider {Provider}."),
    };
}

public enum AiProvider
{
    /// <summary>Free, no account or key. Good for development and demos; do not send real invoices.</summary>
    Pollinations = 1,

    /// <summary>Runs the model on your own machine or server; invoices never leave it.</summary>
    Ollama = 2,

    /// <summary>Paid, needs an API key.</summary>
    OpenAI = 3,
}
