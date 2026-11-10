using Mawrid.Api.Common;
using Mawrid.Api.Common.Errors;
using Mawrid.Api.Common.Security;
using Mawrid.Application.Approvals;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Mawrid.Api.Approvals;

[Route("api/v1/approval-policies")]
[Authorize(Policy = Policies.Admin)]
public sealed class ApprovalPoliciesController : ApiControllerBase
{
    [HttpGet]
    [ProducesResponseType<IReadOnlyList<ApprovalPolicyDto>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> List(CancellationToken cancellationToken) =>
        OkOrProblem(await Sender.Send(new GetApprovalPoliciesQuery(), cancellationToken));

    public sealed record SetPolicyRequest(IReadOnlyList<ApprovalRuleDto> Rules);

    /// <summary>Creates or replaces the matrix for a document type (PurchaseRequisition or PurchaseOrder).</summary>
    [HttpPut("{documentType}")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public async Task<IActionResult> Set(string documentType, SetPolicyRequest request, CancellationToken cancellationToken) =>
        NoContentOrProblem(await Sender.Send(new SetApprovalPolicyCommand(documentType, request.Rules), cancellationToken));
}

[Route("api/v1/approver-groups")]
[Authorize(Policy = Policies.Admin)]
public sealed class ApproverGroupsController : ApiControllerBase
{
    [HttpGet]
    [ProducesResponseType<IReadOnlyList<ApproverGroupDto>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> List(CancellationToken cancellationToken) =>
        OkOrProblem(await Sender.Send(new GetApproverGroupsQuery(), cancellationToken));

    [HttpPost]
    [ProducesResponseType<CreatedId>(StatusCodes.Status201Created)]
    public async Task<IActionResult> Create(CreateApproverGroupCommand command, CancellationToken cancellationToken)
    {
        var result = await Sender.Send(command, cancellationToken);
        return result.IsSuccess ? Created($"/api/v1/approver-groups/{result.Value}", new CreatedId(result.Value)) : result.Error!.ToActionResult();
    }

    public sealed record MembersRequest(IReadOnlyList<Guid> MemberUserIds);

    [HttpPut("{id:guid}/members")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public async Task<IActionResult> ReplaceMembers(Guid id, MembersRequest request, CancellationToken cancellationToken) =>
        NoContentOrProblem(await Sender.Send(new ReplaceApproverGroupMembersCommand(id, request.MemberUserIds), cancellationToken));
}

[Route("api/v1/approval-delegations")]
[Authorize(Policy = Policies.InternalUser)]
public sealed class ApprovalDelegationsController : ApiControllerBase
{
    /// <summary>Delegations I gave or received.</summary>
    [HttpGet]
    [ProducesResponseType<IReadOnlyList<DelegationDto>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> Mine(CancellationToken cancellationToken) =>
        OkOrProblem(await Sender.Send(new GetMyDelegationsQuery(), cancellationToken));

    /// <summary>Hands my approval rights to a colleague for a period.</summary>
    [HttpPost]
    [ProducesResponseType<CreatedId>(StatusCodes.Status201Created)]
    public async Task<IActionResult> Create(CreateDelegationCommand command, CancellationToken cancellationToken)
    {
        var result = await Sender.Send(command, cancellationToken);
        return result.IsSuccess ? Created($"/api/v1/approval-delegations/{result.Value}", new CreatedId(result.Value)) : result.Error!.ToActionResult();
    }

    [HttpPost("{id:guid}/revoke")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public async Task<IActionResult> Revoke(Guid id, CancellationToken cancellationToken) =>
        NoContentOrProblem(await Sender.Send(new RevokeDelegationCommand(id), cancellationToken));
}
