using Mawrid.Api.Common.Errors;
using Mawrid.Application.Common.Exceptions;
using Mawrid.Domain.Common;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Mawrid.Api.Tests.Infrastructure;

/// <summary>Test-only endpoints that fail on purpose, to test the error pipeline.</summary>
[ApiController]
[AllowAnonymous]
[ApiExplorerSettings(IgnoreApi = true)]
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

    /// <summary>Two rows with the same unique key, as when two identical requests race.</summary>
    [HttpGet("duplicate")]
    public async Task<IActionResult> Duplicate([FromServices] Mawrid.Infrastructure.Persistence.AppDbContext db)
    {
        var organizationId = Guid.NewGuid();
        Mawrid.Infrastructure.Idempotency.IdempotencyRecord Record() => new()
        {
            Id = Guid.NewGuid(),
            OrganizationId = organizationId,
            Key = "same-key",
            RequestHash = "hash",
            CreatedAtUtc = DateTimeOffset.UtcNow,
            ExpiresAtUtc = DateTimeOffset.UtcNow.AddHours(1),
        };

        db.IdempotencyRecords.AddRange(Record(), Record());
        await db.SaveChangesAsync();
        return Ok();
    }

    [HttpGet("conflict")]
    public IActionResult DuplicateConflict() =>
        Error.Conflict("supplier.duplicate_vat", "A supplier with this VAT number already exists.").ToActionResult();
}
