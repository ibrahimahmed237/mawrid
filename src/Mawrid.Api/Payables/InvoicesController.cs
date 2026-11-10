using Mawrid.Api.Common;
using Mawrid.Api.Common.Errors;
using Mawrid.Api.Common.Idempotency;
using Mawrid.Api.Common.Security;
using Mawrid.Application.Common.Paging;
using Mawrid.Application.Payables;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Mawrid.Api.Payables;

/// <summary>Supplier invoices from the accounts payable side: the work list, holds and release.</summary>
[Route("api/v1/invoices")]
[Authorize(Policy = Policies.InternalUser)]
[Authorize(Policy = Policies.Payables)]
public sealed class InvoicesController : ApiControllerBase
{
    /// <summary>The work list, for example <c>?status=OnHold</c>.</summary>
    [HttpGet]
    [ProducesResponseType<PagedList<InvoiceSummaryDto>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> List([FromQuery] int page = 1, [FromQuery] int pageSize = 20, [FromQuery] string? status = null,
        [FromQuery] Guid? purchaseOrderId = null, CancellationToken cancellationToken = default) =>
        OkOrProblem(await Sender.Send(new GetInvoicesQuery(new PageRequest(page, pageSize), status, purchaseOrderId), cancellationToken));

    [HttpGet("{id:guid}")]
    [ProducesResponseType<InvoiceDto>(StatusCodes.Status200OK)]
    public async Task<IActionResult> Get(Guid id, CancellationToken cancellationToken) =>
        OkOrProblem(await Sender.Send(new GetInvoiceQuery(id), cancellationToken));

    /// <summary>Types in an invoice that arrived only as a PDF.</summary>
    [HttpPost]
    [Idempotent]
    [ProducesResponseType<CreatedId>(StatusCodes.Status201Created)]
    public async Task<IActionResult> Enter(EnterInvoiceCommand command, CancellationToken cancellationToken)
    {
        var result = await Sender.Send(command, cancellationToken);
        return result.IsSuccess ? CreatedAtAction(nameof(Get), new { id = result.Value }, new CreatedId(result.Value)) : result.Error!.ToActionResult();
    }

    [HttpPost("{id:guid}/recheck")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public async Task<IActionResult> Recheck(Guid id, CancellationToken cancellationToken) =>
        NoContentOrProblem(await Sender.Send(new RecheckInvoiceCommand(id), cancellationToken));

    public sealed record OverrideRequest(string Code, int? LineNumber, string Reason);

    /// <summary>Accepts one hold with a reason. Price holds need an admin.</summary>
    [HttpPost("{id:guid}/overrides")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public async Task<IActionResult> Override(Guid id, OverrideRequest request, CancellationToken cancellationToken) =>
        NoContentOrProblem(await Sender.Send(new OverrideHoldCommand(id, request.Code, request.LineNumber, request.Reason), cancellationToken));

    [HttpPost("{id:guid}/release")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public async Task<IActionResult> Release(Guid id, CancellationToken cancellationToken) =>
        NoContentOrProblem(await Sender.Send(new ReleaseInvoiceCommand(id), cancellationToken));

    public sealed record RejectRequest(string Reason);

    [HttpPost("{id:guid}/reject")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public async Task<IActionResult> Reject(Guid id, RejectRequest request, CancellationToken cancellationToken) =>
        NoContentOrProblem(await Sender.Send(new RejectInvoiceCommand(id, request.Reason), cancellationToken));
}
