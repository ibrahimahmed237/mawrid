using Serilog.Events;

namespace Mawrid.Api.Common.Observability;

/// <summary>Chooses the log level for the one-line-per-request log.</summary>
public static class RequestLogLevel
{
    public static LogEventLevel For(HttpContext context, double elapsedMs, Exception? exception)
    {
        if (exception is not null || context.Response.StatusCode >= StatusCodes.Status500InternalServerError)
        {
            return LogEventLevel.Error;
        }

        // Health probes run every few seconds; at Information they would flood the logs.
        if (context.Request.Path.StartsWithSegments("/health"))
        {
            return LogEventLevel.Verbose;
        }

        return LogEventLevel.Information;
    }
}
