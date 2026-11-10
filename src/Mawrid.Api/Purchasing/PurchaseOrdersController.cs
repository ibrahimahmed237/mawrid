using Mawrid.Api.Common;
using Mawrid.Api.Common.Security;
using Mawrid.Application.Common.Paging;
using Mawrid.Application.Purchasing;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Mawrid.Api.Purchasing;

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
    public async Task<IActionResult> Get(Guid id, CancellationToken cancellationToken) =>
        OkOrProblem(await Sender.Send(new GetPurchaseOrderQuery(id), cancellationToken));
}
