using Mawrid.Api.Common;
using Mawrid.Api.Common.Errors;
using Mawrid.Api.Common.Idempotency;
using Mawrid.Api.Common.Security;
using Mawrid.Application.Common.Paging;
using Mawrid.Application.Sourcing;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Mawrid.Api.Sourcing;

[Route("api/v1/rfqs")]
[Authorize(Policy = Policies.Procurement)]
public sealed class RfqsController : ApiControllerBase
{
    [HttpGet]
    [ProducesResponseType<PagedList<RfqSummaryDto>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> List([FromQuery] int page = 1, [FromQuery] int pageSize = 20, [FromQuery] string? status = null,
        CancellationToken cancellationToken = default) =>
        OkOrProblem(await Sender.Send(new GetRfqsQuery(new PageRequest(page, pageSize), status), cancellationToken));

    [HttpGet("{id:guid}")]
    [ProducesResponseType<RfqDto>(StatusCodes.Status200OK)]
    public async Task<IActionResult> Get(Guid id, CancellationToken cancellationToken) =>
        OkOrProblem(await Sender.Send(new GetRfqQuery(id), cancellationToken));

    /// <summary>Creates a draft RFQ from approved requisitions.</summary>
    [HttpPost]
    [Idempotent]
    [ProducesResponseType<CreatedId>(StatusCodes.Status201Created)]
    public async Task<IActionResult> Create(CreateRfqCommand command, CancellationToken cancellationToken)
    {
        var result = await Sender.Send(command, cancellationToken);
        return result.IsSuccess ? CreatedAtAction(nameof(Get), new { id = result.Value }, new CreatedId(result.Value)) : result.Error!.ToActionResult();
    }

    public sealed record DeadlineRequest(DateTimeOffset Deadline);

    [HttpPut("{id:guid}/deadline")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public async Task<IActionResult> SetDeadline(Guid id, DeadlineRequest request, CancellationToken cancellationToken) =>
        NoContentOrProblem(await Sender.Send(new SetRfqDeadlineCommand(id, request.Deadline), cancellationToken));

    public sealed record InviteRequest(IReadOnlyList<Guid> SupplierIds);

    [HttpPost("{id:guid}/invitations")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public async Task<IActionResult> Invite(Guid id, InviteRequest request, CancellationToken cancellationToken) =>
        NoContentOrProblem(await Sender.Send(new InviteSuppliersCommand(id, request.SupplierIds), cancellationToken));

    [HttpDelete("{id:guid}/invitations/{supplierId:guid}")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public async Task<IActionResult> RemoveInvitation(Guid id, Guid supplierId, CancellationToken cancellationToken) =>
        NoContentOrProblem(await Sender.Send(new RemoveInvitationCommand(id, supplierId), cancellationToken));

    public sealed record PublishRequest(string? SingleSourceReason);

    [HttpPost("{id:guid}/publish")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public async Task<IActionResult> Publish(Guid id, PublishRequest request, CancellationToken cancellationToken) =>
        NoContentOrProblem(await Sender.Send(new PublishRfqCommand(id, request.SingleSourceReason), cancellationToken));

    public sealed record AmendRequest(DateTimeOffset Deadline, IReadOnlyDictionary<int, decimal>? Quantities);

    [HttpPost("{id:guid}/amend")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public async Task<IActionResult> Amend(Guid id, AmendRequest request, CancellationToken cancellationToken) =>
        NoContentOrProblem(await Sender.Send(new AmendRfqCommand(id, request.Deadline, request.Quantities), cancellationToken));

    public sealed record CancelRequest(string Reason);

    [HttpPost("{id:guid}/cancel")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public async Task<IActionResult> Cancel(Guid id, CancelRequest request, CancellationToken cancellationToken) =>
        NoContentOrProblem(await Sender.Send(new CancelRfqCommand(id, request.Reason), cancellationToken));

    /// <summary>Who submitted and when. No prices: they stay sealed until the deadline.</summary>
    [HttpGet("{id:guid}/submissions")]
    [ProducesResponseType<IReadOnlyList<SubmissionDto>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> Submissions(Guid id, CancellationToken cancellationToken) =>
        OkOrProblem(await Sender.Send(new GetRfqSubmissionsQuery(id), cancellationToken));

    /// <summary>Side-by-side prices, after the deadline only.</summary>
    [HttpGet("{id:guid}/comparison")]
    [ProducesResponseType<ComparisonDto>(StatusCodes.Status200OK)]
    public async Task<IActionResult> Comparison(Guid id, CancellationToken cancellationToken) =>
        OkOrProblem(await Sender.Send(new GetRfqComparisonQuery(id), cancellationToken));

    public sealed record AwardRequest(IReadOnlyList<AwardLineDto> Lines);

    public sealed record AwardResponse(IReadOnlyList<Guid> PurchaseOrderIds);

    /// <summary>Awards items and creates one draft PO per winning supplier.</summary>
    [HttpPost("{id:guid}/award")]
    [Idempotent]
    [ProducesResponseType<AwardResponse>(StatusCodes.Status200OK)]
    public async Task<IActionResult> Award(Guid id, AwardRequest request, CancellationToken cancellationToken)
    {
        var result = await Sender.Send(new AwardRfqCommand(id, request.Lines), cancellationToken);
        return result.IsSuccess ? Ok(new AwardResponse(result.Value)) : result.Error!.ToActionResult();
    }
}
