using Mawrid.Api.Common;
using Mawrid.Api.Common.Security;
using Mawrid.Application.Common.Paging;
using Mawrid.Application.Purchasing;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace Mawrid.Api.Purchasing;

/// <summary>Purchase orders from the supplier's side. A supplier sees only its own POs, once sent.</summary>
[Route("api/v1/supplier/purchase-orders")]
[Authorize(Policy = Policies.SupplierUser)]
[EnableRateLimiting(RateLimits.SupplierPortal)]
public sealed class SupplierPurchaseOrdersController : ApiControllerBase
{
    [HttpGet]
    [ProducesResponseType<PagedList<PurchaseOrderSummaryDto>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> List([FromQuery] int page = 1, [FromQuery] int pageSize = 20, [FromQuery] string? status = null,
        CancellationToken cancellationToken = default) =>
        OkOrProblem(await Sender.Send(new GetPurchaseOrdersQuery(new PageRequest(page, pageSize), status), cancellationToken));

    [HttpGet("{id:guid}")]
    [ProducesResponseType<SupplierPurchaseOrderDto>(StatusCodes.Status200OK)]
    public async Task<IActionResult> Get(Guid id, CancellationToken cancellationToken) =>
        OkOrProblem(await Sender.Send(new GetSupplierPurchaseOrderQuery(id), cancellationToken));

    [HttpPost("{id:guid}/acknowledge")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public async Task<IActionResult> Acknowledge(Guid id, CancellationToken cancellationToken) =>
        NoContentOrProblem(await Sender.Send(new AcknowledgePurchaseOrderCommand(id), cancellationToken));
}
