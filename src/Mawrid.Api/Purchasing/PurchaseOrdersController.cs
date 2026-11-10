using Mawrid.Api.Common;
using Mawrid.Api.Common.Errors;
using Mawrid.Api.Common.Idempotency;
using Mawrid.Api.Common.Security;
using Mawrid.Application.Common.Paging;
using Mawrid.Application.Purchasing;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Mawrid.Api.Purchasing;

/// <summary>Purchase orders from the buyer's side. State changes accept If-Match with the ETag from GET.</summary>
[Route("api/v1/purchase-orders")]
[Authorize(Policy = Policies.InternalUser)]
public sealed class PurchaseOrdersController : ApiControllerBase
{
    [HttpGet]
    [ProducesResponseType<PagedList<PurchaseOrderSummaryDto>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> List([FromQuery] int page = 1, [FromQuery] int pageSize = 20, [FromQuery] string? status = null,
        CancellationToken cancellationToken = default) =>
        OkOrProblem(await Sender.Send(new GetPurchaseOrdersQuery(new PageRequest(page, pageSize), status), cancellationToken));

    [HttpGet("{id:guid}")]
    [ProducesResponseType<PurchaseOrderDto>(StatusCodes.Status200OK)]
    public async Task<IActionResult> Get(Guid id, CancellationToken cancellationToken)
    {
        var result = await Sender.Send(new GetPurchaseOrderQuery(id), cancellationToken);
        if (result.IsSuccess)
        {
            Response.Headers.ETag = ETags.Format(result.Value.Version);
        }

        return OkOrProblem(result);
    }

    /// <summary>Creates a draft PO from a requisition routed to direct purchase.</summary>
    [HttpPost]
    [Idempotent]
    [Authorize(Policy = Policies.Procurement)]
    [ProducesResponseType<CreatedId>(StatusCodes.Status201Created)]
    public async Task<IActionResult> CreateDirect(CreateDirectPurchaseOrderCommand command, CancellationToken cancellationToken)
    {
        var result = await Sender.Send(command, cancellationToken);
        return result.IsSuccess ? CreatedAtAction(nameof(Get), new { id = result.Value }, new CreatedId(result.Value)) : result.Error!.ToActionResult();
    }

    public sealed record TermsRequest(int PaymentTermsDays, string? DeliveryAddress);

    [HttpPut("{id:guid}/terms")]
    [Authorize(Policy = Policies.Procurement)]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public async Task<IActionResult> UpdateTerms(Guid id, TermsRequest request, CancellationToken cancellationToken) =>
        NoContentOrProblem(await Sender.Send(
            new UpdatePurchaseOrderTermsCommand(id, request.PaymentTermsDays, request.DeliveryAddress, ETags.FromIfMatch(Request)), cancellationToken));

    [HttpPost("{id:guid}/submit")]
    [Authorize(Policy = Policies.Procurement)]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public async Task<IActionResult> Submit(Guid id, CancellationToken cancellationToken) =>
        NoContentOrProblem(await Sender.Send(new SubmitPurchaseOrderCommand(id, ETags.FromIfMatch(Request)), cancellationToken));

    [HttpPost("{id:guid}/send")]
    [Authorize(Policy = Policies.Procurement)]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public async Task<IActionResult> Send(Guid id, CancellationToken cancellationToken) =>
        NoContentOrProblem(await Sender.Send(new SendPurchaseOrderCommand(id, ETags.FromIfMatch(Request)), cancellationToken));

    public sealed record ReasonRequest(string Reason);

    [HttpPost("{id:guid}/cancel")]
    [Authorize(Policy = Policies.Procurement)]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public async Task<IActionResult> Cancel(Guid id, ReasonRequest request, CancellationToken cancellationToken) =>
        NoContentOrProblem(await Sender.Send(new CancelPurchaseOrderCommand(id, request.Reason, ETags.FromIfMatch(Request)), cancellationToken));

    public sealed record CloseRequest(string? Reason);

    /// <summary>Closes a finished PO, or short closes a partly received one (reason required).</summary>
    [HttpPost("{id:guid}/close")]
    [Authorize(Policy = Policies.Procurement)]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public async Task<IActionResult> Close(Guid id, CloseRequest request, CancellationToken cancellationToken) =>
        NoContentOrProblem(await Sender.Send(new ClosePurchaseOrderCommand(id, request.Reason, ETags.FromIfMatch(Request)), cancellationToken));

    public sealed record ReceiptRequest(IReadOnlyList<ReceiptLineDto> Lines, string? Note);

    /// <summary>Records a delivery (goods receipt). Returns the receipt id.</summary>
    [HttpPost("{id:guid}/receipts")]
    [Idempotent]
    [Authorize(Policy = Policies.Receiving)]
    [ProducesResponseType<CreatedId>(StatusCodes.Status201Created)]
    public async Task<IActionResult> Receive(Guid id, ReceiptRequest request, CancellationToken cancellationToken)
    {
        var result = await Sender.Send(new RecordGoodsReceiptCommand(id, request.Lines, request.Note), cancellationToken);
        return result.IsSuccess ? CreatedAtAction(nameof(Get), new { id }, new CreatedId(result.Value)) : result.Error!.ToActionResult();
    }
}
