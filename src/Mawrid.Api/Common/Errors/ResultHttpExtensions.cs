using Mawrid.Domain.Common;
using Microsoft.AspNetCore.Mvc;

namespace Mawrid.Api.Common.Errors;

/// <summary>Turns a failed <see cref="Result"/> into an RFC 7807 ProblemDetails response.</summary>
public static class ResultHttpExtensions
{
    public static int ToStatusCode(this ErrorType type) => type switch
    {
        ErrorType.Validation => StatusCodes.Status400BadRequest,
        ErrorType.Unauthorized => StatusCodes.Status401Unauthorized,
        ErrorType.Forbidden => StatusCodes.Status403Forbidden,
        ErrorType.NotFound => StatusCodes.Status404NotFound,
        ErrorType.Conflict => StatusCodes.Status409Conflict,
        ErrorType.RuleViolation => StatusCodes.Status422UnprocessableEntity,
        ErrorType.Precondition => StatusCodes.Status412PreconditionFailed,
        _ => StatusCodes.Status500InternalServerError,
    };

    public static ProblemDetails ToProblemDetails(this Error error)
    {
        var problem = new ProblemDetails
        {
            Status = error.Type.ToStatusCode(),
            Title = error.Type.ToString(),
            Detail = error.Message,
        };
        problem.Extensions["code"] = error.Code;
        return problem;
    }

    /// <summary>
    /// Use in controllers: <c>return result.IsSuccess ? Ok(result.Value) : result.Error!.ToActionResult();</c>
    /// The response goes through <see cref="IProblemDetailsService"/>, so it gets the same
    /// <c>traceId</c> and format as every other error.
    /// </summary>
    public static IActionResult ToActionResult(this Error error) => new ProblemActionResult(error.ToProblemDetails());

    private sealed class ProblemActionResult(ProblemDetails problem) : IActionResult
    {
        public async Task ExecuteResultAsync(ActionContext context)
        {
            var httpContext = context.HttpContext;
            httpContext.Response.StatusCode = problem.Status!.Value;

            var service = httpContext.RequestServices.GetRequiredService<IProblemDetailsService>();
            await service.WriteAsync(new ProblemDetailsContext { HttpContext = httpContext, ProblemDetails = problem });
        }
    }
}
