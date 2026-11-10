using Mawrid.Api.Common;
using Mawrid.Api.Common.Errors;
using Mawrid.Api.Common.Idempotency;
using Mawrid.Api.Common.Security;
using Mawrid.Application.Common.Paging;
using Mawrid.Application.Requisitions;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Mawrid.Api.Requisitions;

[Route("api/v1/requisitions")]
[Authorize(Policy = Policies.InternalUser)]
public sealed class RequisitionsController : ApiControllerBase
{
    [HttpGet]
    [ProducesResponseType<PagedList<RequisitionSummaryDto>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> List(
        [FromQuery] int page = 1, [FromQuery] int pageSize = 20, [FromQuery] string? status = null, CancellationToken cancellationToken = default) =>
        OkOrProblem(await Sender.Send(new GetRequisitionsQuery(new PageRequest(page, pageSize), status), cancellationToken));

    [HttpGet("{id:guid}")]
    [ProducesResponseType<RequisitionDto>(StatusCodes.Status200OK)]
    public async Task<IActionResult> Get(Guid id, CancellationToken cancellationToken) =>
        OkOrProblem(await Sender.Send(new GetRequisitionQuery(id), cancellationToken));

    [HttpPost]
    [Idempotent]
    [ProducesResponseType<CreatedId>(StatusCodes.Status201Created)]
    public async Task<IActionResult> Create(CreateRequisitionCommand command, CancellationToken cancellationToken)
    {
        var result = await Sender.Send(command, cancellationToken);
        return result.IsSuccess
            ? CreatedAtAction(nameof(Get), new { id = result.Value }, new CreatedId(result.Value))
            : result.Error!.ToActionResult();
    }

    public sealed record UpdateRequest(Guid CostCenterId, string Title, string Justification, DateOnly NeededBy, IReadOnlyList<RequisitionLineDto> Lines);

    [HttpPut("{id:guid}")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public async Task<IActionResult> Update(Guid id, UpdateRequest request, CancellationToken cancellationToken) =>
        NoContentOrProblem(await Sender.Send(
            new UpdateRequisitionCommand(id, request.CostCenterId, request.Title, request.Justification, request.NeededBy, request.Lines), cancellationToken));

    [HttpPost("{id:guid}/submit")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public async Task<IActionResult> Submit(Guid id, CancellationToken cancellationToken) =>
        NoContentOrProblem(await Sender.Send(new SubmitRequisitionCommand(id), cancellationToken));

    [HttpPost("{id:guid}/cancel")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public async Task<IActionResult> Cancel(Guid id, CancellationToken cancellationToken) =>
        NoContentOrProblem(await Sender.Send(new CancelRequisitionCommand(id), cancellationToken));

    public sealed record SourcingRouteRequest(string Route);

    [HttpPost("{id:guid}/sourcing-route")]
    [Authorize(Policy = Policies.Procurement)]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public async Task<IActionResult> ChooseSourcingRoute(Guid id, SourcingRouteRequest request, CancellationToken cancellationToken) =>
        NoContentOrProblem(await Sender.Send(new ChooseSourcingRouteCommand(id, request.Route), cancellationToken));
}
