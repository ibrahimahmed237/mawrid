using Mawrid.Api.Common.Errors;
using Mawrid.Application.Common.Exceptions;
using Mawrid.Domain.Common;
using Microsoft.AspNetCore.Mvc;

namespace Mawrid.Api.Tests.Infrastructure;

/// <summary>Test-only endpoints that fail on purpose, to test the error pipeline.</summary>
[ApiController]
[Route("_test/errors")]
public sealed class TestErrorsController : ControllerBase
{
    [HttpGet("unhandled")]
    public IActionResult Unhandled() =>
        throw new InvalidOperationException("Secret internal detail that must not leak.");

    [HttpGet("validation")]
    public IActionResult Validation() =>
        throw new RequestValidationException(new Dictionary<string, string[]>
        {
            ["Name"] = ["'Name' must not be empty."],
        });

    [HttpGet("conflict")]
    public IActionResult DuplicateConflict() =>
        Error.Conflict("supplier.duplicate_vat", "A supplier with this VAT number already exists.").ToActionResult();
}
