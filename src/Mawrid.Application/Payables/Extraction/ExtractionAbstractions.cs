using Mawrid.Domain.Common;

namespace Mawrid.Application.Payables.Extraction;

/// <summary>
/// A chat model that answers with JSON (OpenAI-compatible API: Pollinations, Ollama or OpenAI).
/// Returns <see cref="ExtractionErrors.ProviderUnavailable"/> when the provider cannot be reached.
/// </summary>
public interface ILlmClient
{
    Task<Result<LlmAnswer>> CompleteJsonAsync(string systemPrompt, string userPrompt, CancellationToken cancellationToken);
}

public sealed record LlmAnswer(string Content, string Provider, string Model);

/// <summary>Reads the text layer of a PDF. Scanned PDFs (images only) give an empty string.</summary>
public interface IPdfTextReader
{
    /// <returns>The text, or null when the file is not a readable PDF.</returns>
    Task<string?> ReadTextAsync(Stream pdf, CancellationToken cancellationToken);
}

public static class ExtractionErrors
{
    public static readonly Error NotAPdf = Error.Validation("extraction.not_pdf", "The file is not a readable PDF.");

    public static readonly Error NoText = Error.RuleViolation(
        "extraction.no_text", "The PDF has no text layer (it is probably a scan). Type the invoice in by hand.");

    public static readonly Error ProviderUnavailable = Error.Unavailable(
        "extraction.provider_unavailable", "The AI service did not answer. Try again later or type the invoice in by hand.");

    public static readonly Error UnreadableAnswer = Error.Unavailable(
        "extraction.unreadable_answer", "The AI service gave an answer we could not read. Try again or type the invoice in by hand.");
}
