using Mawrid.Api.Common;
using Mawrid.Api.Common.Security;
using Mawrid.Application.Approvals;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Mawrid.Api.Approvals;

[Route("api/v1/approvals")]
[Authorize(Policy = Policies.InternalUser)]
public sealed class ApprovalsController : ApiControllerBase
{
    /// <summary>Approvals waiting for me (as approver or as delegate).</summary>
    [HttpGet("inbox")]
    [ProducesResponseType<IReadOnlyList<InboxItemDto>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> Inbox(CancellationToken cancellationToken) =>
        OkOrProblem(await Sender.Send(new GetApprovalInboxQuery(), cancellationToken));

    /// <summary>One approval with all steps. The ETag header carries the version for If-Match.</summary>
    [HttpGet("{id:guid}")]
    [ProducesResponseType<ApprovalDto>(StatusCodes.Status200OK)]
    public async Task<IActionResult> Get(Guid id, CancellationToken cancellationToken)
    {
        var result = await Sender.Send(new GetApprovalQuery(id), cancellationToken);
        if (result.IsSuccess)
        {
            Response.Headers.ETag = ETags.Format(result.Value.Version);
        }

        return OkOrProblem(result);
    }

    public sealed record ApproveRequest(string? Comment);

    public sealed record RejectRequest(string Reason);

    [HttpPost("{id:guid}/approve")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public async Task<IActionResult> Approve(Guid id, ApproveRequest request, CancellationToken cancellationToken) =>
        NoContentOrProblem(await Sender.Send(new DecideApprovalCommand(id, true, request.Comment, ETags.FromIfMatch(Request)), cancellationToken));

    [HttpPost("{id:guid}/reject")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public async Task<IActionResult> Reject(Guid id, RejectRequest request, CancellationToken cancellationToken) =>
        NoContentOrProblem(await Sender.Send(new DecideApprovalCommand(id, false, request.Reason, ETags.FromIfMatch(Request)), cancellationToken));
}
