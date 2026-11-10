using System.Diagnostics;
using Mawrid.Domain.Common;
using MediatR;
using Microsoft.Extensions.Logging;

namespace Mawrid.Application.Common.Behaviors;

/// <summary>
/// Logs every request with its name, duration and outcome. Failed results are
/// logged with their error code, so support can search logs by code.
/// </summary>
public sealed partial class LoggingBehavior<TRequest, TResponse>(ILogger<LoggingBehavior<TRequest, TResponse>> logger)
    : IPipelineBehavior<TRequest, TResponse>
    where TRequest : notnull
{
    public async Task<TResponse> Handle(
        TRequest request,
        RequestHandlerDelegate<TResponse> next,
        CancellationToken cancellationToken)
    {
        var requestName = typeof(TRequest).Name;
        var started = Stopwatch.GetTimestamp();

        var response = await next(cancellationToken);

        var elapsedMs = Stopwatch.GetElapsedTime(started).TotalMilliseconds;

        if (response is Result { IsFailure: true } failed)
        {
            LogFailed(requestName, failed.Error!.Code, elapsedMs);
        }
        else
        {
            LogHandled(requestName, elapsedMs);
        }

        return response;
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "Handled {RequestName} in {ElapsedMs:0.0} ms")]
    private partial void LogHandled(string requestName, double elapsedMs);

    [LoggerMessage(Level = LogLevel.Warning, Message = "{RequestName} failed with {ErrorCode} in {ElapsedMs:0.0} ms")]
    private partial void LogFailed(string requestName, string errorCode, double elapsedMs);
}
