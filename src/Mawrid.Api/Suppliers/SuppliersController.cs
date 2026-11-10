using Mawrid.Api.Common;
using Mawrid.Api.Common.Errors;
using Mawrid.Api.Common.Idempotency;
using Mawrid.Api.Common.Security;
using Mawrid.Application.Common.Paging;
using Mawrid.Application.Suppliers;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Mawrid.Api.Suppliers;

[Route("api/v1/suppliers")]
[Authorize(Policy = Policies.InternalUser)]
public sealed class SuppliersController : ApiControllerBase
{
    [HttpGet]
    [ProducesResponseType<PagedList<SupplierSummaryDto>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> List(
        [FromQuery] int page = 1, [FromQuery] int pageSize = 20, [FromQuery] string? status = null, [FromQuery] string? search = null,
        CancellationToken cancellationToken = default) =>
        OkOrProblem(await Sender.Send(new GetSuppliersQuery(new PageRequest(page, pageSize), status, search), cancellationToken));

    [HttpGet("{id:guid}")]
    [ProducesResponseType<SupplierDto>(StatusCodes.Status200OK)]
    public async Task<IActionResult> Get(Guid id, CancellationToken cancellationToken) =>
        OkOrProblem(await Sender.Send(new GetSupplierQuery(id), cancellationToken));

    [HttpPost]
    [Idempotent]
    [Authorize(Policy = Policies.Procurement)]
    [ProducesResponseType<CreatedId>(StatusCodes.Status201Created)]
    public async Task<IActionResult> Register(RegisterSupplierCommand command, CancellationToken cancellationToken)
    {
        var result = await Sender.Send(command, cancellationToken);
        return result.IsSuccess
            ? CreatedAtAction(nameof(Get), new { id = result.Value }, new CreatedId(result.Value))
            : result.Error!.ToActionResult();
    }

    public sealed record ReasonRequest(string Reason);

    [HttpPost("{id:guid}/approve")]
    [Authorize(Policy = Policies.Admin)]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public async Task<IActionResult> Approve(Guid id, CancellationToken cancellationToken) =>
        NoContentOrProblem(await Sender.Send(new DecideSupplierCommand(id, SupplierDecision.Approve), cancellationToken));

    [HttpPost("{id:guid}/reject")]
    [Authorize(Policy = Policies.Admin)]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public async Task<IActionResult> Reject(Guid id, ReasonRequest request, CancellationToken cancellationToken) =>
        NoContentOrProblem(await Sender.Send(new DecideSupplierCommand(id, SupplierDecision.Reject, request.Reason), cancellationToken));

    [HttpPost("{id:guid}/block")]
    [Authorize(Policy = Policies.Admin)]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public async Task<IActionResult> Block(Guid id, ReasonRequest request, CancellationToken cancellationToken) =>
        NoContentOrProblem(await Sender.Send(new DecideSupplierCommand(id, SupplierDecision.Block, request.Reason), cancellationToken));

    [HttpPost("{id:guid}/unblock")]
    [Authorize(Policy = Policies.Admin)]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public async Task<IActionResult> Unblock(Guid id, CancellationToken cancellationToken) =>
        NoContentOrProblem(await Sender.Send(new DecideSupplierCommand(id, SupplierDecision.Unblock), cancellationToken));

    [HttpPost("{id:guid}/bank-accounts/{bankAccountId:guid}/verify")]
    [Authorize(Policy = Policies.Admin)]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public async Task<IActionResult> VerifyBankAccount(Guid id, Guid bankAccountId, CancellationToken cancellationToken) =>
        NoContentOrProblem(await Sender.Send(new VerifyBankAccountCommand(id, bankAccountId), cancellationToken));
}
