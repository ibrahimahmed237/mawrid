using Mawrid.Api.Common;
using Mawrid.Api.Common.Errors;
using Mawrid.Api.Common.Security;
using Mawrid.Application.Sourcing;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace Mawrid.Api.Sourcing;

/// <summary>RFQs from the supplier's side.</summary>
[Route("api/v1/supplier/rfqs")]
[Authorize(Policy = Policies.SupplierUser)]
[EnableRateLimiting(RateLimits.SupplierPortal)]
public sealed class SupplierRfqsController : ApiControllerBase
{
    [HttpGet]
    [ProducesResponseType<IReadOnlyList<SupplierRfqDto>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> List(CancellationToken cancellationToken) =>
        OkOrProblem(await Sender.Send(new GetMyRfqsQuery(), cancellationToken));

    [HttpGet("{id:guid}")]
    [ProducesResponseType<SupplierRfqDto>(StatusCodes.Status200OK)]
    public async Task<IActionResult> Get(Guid id, CancellationToken cancellationToken) =>
        OkOrProblem(await Sender.Send(new GetMyRfqQuery(id), cancellationToken));

    public sealed record QuotationRequest(DateOnly ValidUntil, IReadOnlyList<QuotationLineDto> Lines, string? Notes, IReadOnlyList<Guid>? AttachmentFileIds);

    /// <summary>Submits or revises my quotation. Works until the deadline.</summary>
    [HttpPut("{id:guid}/quotation")]
    [ProducesResponseType<CreatedId>(StatusCodes.Status200OK)]
    public async Task<IActionResult> SubmitQuotation(Guid id, QuotationRequest request, CancellationToken cancellationToken)
    {
        var result = await Sender.Send(new SubmitQuotationCommand(id, request.ValidUntil, request.Lines, request.Notes, request.AttachmentFileIds), cancellationToken);
        return result.IsSuccess ? Ok(new CreatedId(result.Value)) : result.Error!.ToActionResult();
    }
}
