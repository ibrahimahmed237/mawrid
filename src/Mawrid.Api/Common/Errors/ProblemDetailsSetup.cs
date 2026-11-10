namespace Mawrid.Api.Common.Errors;

public static class ProblemDetailsSetup
{
    /// <summary>
    /// Every error response is ProblemDetails with a <c>traceId</c> and a stable <c>code</c>.
    /// </summary>
    public static IServiceCollection AddApiProblemDetails(this IServiceCollection services)
    {
        services.AddProblemDetails(options => options.CustomizeProblemDetails = context =>
        {
            var problem = context.ProblemDetails;
            problem.Extensions["traceId"] = context.HttpContext.TraceIdentifier;

            // Errors created by the framework itself (404, 405, ...) get a code too.
            if (!problem.Extensions.ContainsKey("code"))
            {
                problem.Extensions["code"] = problem.Status switch
                {
                    StatusCodes.Status401Unauthorized => ErrorCodes.Unauthenticated,
                    StatusCodes.Status403Forbidden => ErrorCodes.Forbidden,
                    StatusCodes.Status404NotFound => ErrorCodes.NotFound,
                    StatusCodes.Status405MethodNotAllowed => ErrorCodes.MethodNotAllowed,
                    StatusCodes.Status400BadRequest => ErrorCodes.ValidationFailed,
                    _ => ErrorCodes.Http,
                };
            }
        });

        services.AddExceptionHandler<GlobalExceptionHandler>();
        return services;
    }
}
