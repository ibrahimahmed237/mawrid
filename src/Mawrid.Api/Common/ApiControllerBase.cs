using Mawrid.Api.Common.Errors;
using Mawrid.Domain.Common;
using MediatR;
using Microsoft.AspNetCore.Mvc;

namespace Mawrid.Api.Common;

/// <summary>Base for all controllers: JSON in and out, errors as ProblemDetails (application/problem+json).</summary>
[ApiController]
[ProducesResponseType<ProblemDetails>(StatusCodes.Status400BadRequest, "application/problem+json")]
[ProducesResponseType<ProblemDetails>(StatusCodes.Status401Unauthorized, "application/problem+json")]
public abstract class ApiControllerBase : ControllerBase
{
    private ISender? _sender;

    protected ISender Sender => _sender ??= HttpContext.RequestServices.GetRequiredService<ISender>();

    /// <summary>200 with the value, or the error as ProblemDetails.</summary>
    protected IActionResult OkOrProblem<T>(Result<T> result) =>
        result.IsSuccess ? Ok(result.Value) : result.Error!.ToActionResult();

    /// <summary>204, or the error as ProblemDetails.</summary>
    protected IActionResult NoContentOrProblem(Result result) =>
        result.IsSuccess ? NoContent() : result.Error!.ToActionResult();
}
