using Mawrid.Application.Common.Exceptions;
using Mawrid.Infrastructure.Persistence;
using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Mawrid.Api.Common.Errors;

/// <summary>
/// The last line of defense. Validation errors become 400 with field errors.
/// Anything else is a bug or an infrastructure failure: we log it and return a
/// generic 500, never the exception details.
/// </summary>
public sealed partial class GlobalExceptionHandler(
    IProblemDetailsService problemDetailsService,
    ILogger<GlobalExceptionHandler> logger) : IExceptionHandler
{
    public async ValueTask<bool> TryHandleAsync(HttpContext httpContext, Exception exception, CancellationToken cancellationToken)
    {
        // The client closed the connection. Nobody is waiting for a response, and it is not our bug.
        if (exception is OperationCanceledException && httpContext.RequestAborted.IsCancellationRequested)
        {
            httpContext.Response.StatusCode = StatusCodes.Status499ClientClosedRequest;
            return true;
        }

        ProblemDetails problem;

        if (exception is RequestValidationException validation)
        {
            problem = new ValidationProblemDetails(validation.Errors.ToDictionary(e => e.Key, e => e.Value))
            {
                Status = StatusCodes.Status400BadRequest,
                Title = "Validation",
                Detail = validation.Message,
            };
            problem.Extensions["code"] = ErrorCodes.ValidationFailed;
        }
        else if (exception is DbUpdateConcurrencyException)
        {
            // Two people saved the same record at the same time. Not a bug; the second one must reload.
            problem = new ProblemDetails
            {
                Status = StatusCodes.Status409Conflict,
                Title = "Conflict",
                Detail = "The record was changed by someone else. Reload it and try again.",
            };
            problem.Extensions["code"] = ErrorCodes.ConcurrencyConflict;
        }
        else if (exception is DbUpdateException update && DbExceptions.IsUniqueViolation(update))
        {
            // The duplicate check passed for two requests at once; the unique index stopped the second.
            problem = new ProblemDetails
            {
                Status = StatusCodes.Status409Conflict,
                Title = "Conflict",
                Detail = "A record with the same unique values already exists.",
            };
            problem.Extensions["code"] = ErrorCodes.Duplicate;
        }
        else
        {
            LogUnhandled(exception, httpContext.Request.Method, httpContext.Request.Path);
            problem = new ProblemDetails
            {
                Status = StatusCodes.Status500InternalServerError,
                Title = "Unexpected error",
                Detail = "Something went wrong on our side. Use the traceId when you contact support.",
            };
            problem.Extensions["code"] = ErrorCodes.Unexpected;
        }

        httpContext.Response.StatusCode = problem.Status!.Value;

        return await problemDetailsService.TryWriteAsync(new ProblemDetailsContext
        {
            HttpContext = httpContext,
            ProblemDetails = problem,
            Exception = exception,
        });
    }

    [LoggerMessage(Level = LogLevel.Error, Message = "Unhandled exception for {Method} {Path}")]
    private partial void LogUnhandled(Exception exception, string method, string path);
}
