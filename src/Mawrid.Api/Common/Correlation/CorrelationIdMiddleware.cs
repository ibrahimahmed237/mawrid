using Serilog.Context;

namespace Mawrid.Api.Common.Correlation;

/// <summary>
/// Gives every request a correlation id. It reuses the caller's <c>X-Correlation-Id</c>
/// header when it is valid, adds the id to every log line, and returns it in the
/// response header, so support can find all logs for one request.
/// </summary>
public sealed class CorrelationIdMiddleware(RequestDelegate next)
{
    public const string HeaderName = "X-Correlation-Id";
    private const int MaxLength = 64;

    public async Task InvokeAsync(HttpContext context)
    {
        var correlationId = ReadOrCreate(context);
        context.TraceIdentifier = correlationId;
        context.Response.Headers[HeaderName] = correlationId;

        using (LogContext.PushProperty("CorrelationId", correlationId))
        {
            await next(context);
        }
    }

    private static string ReadOrCreate(HttpContext context)
    {
        var incoming = context.Request.Headers[HeaderName].ToString();
        return IsValid(incoming) ? incoming : Guid.NewGuid().ToString("N");
    }

    // Only short, simple ids are accepted, so a caller cannot inject text into our logs.
    private static bool IsValid(string value) =>
        value.Length is > 0 and <= MaxLength && value.All(c => char.IsAsciiLetterOrDigit(c) || c is '-' or '_');
}
