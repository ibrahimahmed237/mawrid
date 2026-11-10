using FluentValidation;
using Mawrid.Application.Common.Abstractions;
using Mawrid.Application.Common.Messaging;
using Mawrid.Application.Common.Persistence;
using Mawrid.Application.Common.Security;
using Mawrid.Application.Contracts.Approvals;
using Mawrid.Application.Contracts.Identity;
using Mawrid.Domain.Approvals;
using Mawrid.Domain.Common;
using Microsoft.EntityFrameworkCore;

namespace Mawrid.Application.Approvals;

// ---------- Approval policies (admins) ----------

public sealed record ApprovalStepTemplateDto(string Name, string Kind, Guid? GroupId = null);

public sealed record ApprovalRuleDto(decimal? UpToAmount, IReadOnlyList<ApprovalStepTemplateDto> Steps);

public sealed record ApprovalPolicyDto(Guid Id, string DocumentType, IReadOnlyList<ApprovalRuleDto> Rules);

public sealed record GetApprovalPoliciesQuery : IQuery<IReadOnlyList<ApprovalPolicyDto>>;

internal sealed class GetApprovalPoliciesQueryHandler(IAppDbContext db) : IQueryHandler<GetApprovalPoliciesQuery, IReadOnlyList<ApprovalPolicyDto>>
{
    public async Task<Result<IReadOnlyList<ApprovalPolicyDto>>> Handle(GetApprovalPoliciesQuery query, CancellationToken cancellationToken)
    {
        var policies = await db.ApprovalPolicies.AsNoTracking().OrderBy(p => p.DocumentType).ToListAsync(cancellationToken);
        return policies.Select(p => new ApprovalPolicyDto(p.Id, p.DocumentType,
            p.Rules.Select(r => new ApprovalRuleDto(r.UpToAmount,
                r.Steps.Select(s => new ApprovalStepTemplateDto(s.Name, s.Kind.ToString(), s.GroupId)).ToList())).ToList())).ToList();
    }
}

/// <summary>Creates or replaces the approval matrix of one document type.</summary>
public sealed record SetApprovalPolicyCommand(string DocumentType, IReadOnlyList<ApprovalRuleDto> Rules) : ICommand;

internal sealed class SetApprovalPolicyCommandValidator : AbstractValidator<SetApprovalPolicyCommand>
{
    public SetApprovalPolicyCommandValidator()
    {
        RuleFor(x => x.DocumentType).Must(t => ApprovalDocumentTypes.All.Contains(t))
            .WithMessage($"Document type must be one of: {string.Join(", ", ApprovalDocumentTypes.All)}.");
        RuleFor(x => x.Rules).NotEmpty();
        RuleForEach(x => x.Rules).ChildRules(rule =>
        {
            rule.RuleFor(r => r.Steps).NotEmpty();
            rule.RuleForEach(r => r.Steps).ChildRules(step =>
            {
                step.RuleFor(s => s.Name).NotEmpty().MaximumLength(100);
                step.RuleFor(s => s.Kind).Must(k => Enum.TryParse<ApprovalStepKind>(k, ignoreCase: true, out _))
                    .WithMessage($"Kind must be one of: {string.Join(", ", Enum.GetNames<ApprovalStepKind>())}.");
            });
        });
    }
}

internal sealed class SetApprovalPolicyCommandHandler(IAppDbContext db, ICurrentUser currentUser) : ICommandHandler<SetApprovalPolicyCommand>
{
    public async Task<Result> Handle(SetApprovalPolicyCommand command, CancellationToken cancellationToken)
    {
        var rules = command.Rules.Select(r => new ApprovalRule(r.UpToAmount,
            r.Steps.Select(s => new ApprovalStepTemplate(s.Name.Trim(), Enum.Parse<ApprovalStepKind>(s.Kind, ignoreCase: true), s.GroupId)).ToList())).ToList();

        var groupIds = rules.SelectMany(r => r.Steps).Where(s => s.GroupId is not null).Select(s => s.GroupId!.Value).Distinct().ToList();
        if (await db.ApproverGroups.CountAsync(g => groupIds.Contains(g.Id), cancellationToken) != groupIds.Count)
        {
            return ApprovalErrors.GroupNotFound;
        }

        var policy = await db.ApprovalPolicies.SingleOrDefaultAsync(p => p.DocumentType == command.DocumentType, cancellationToken);
        Result result;
        if (policy is null)
        {
            var created = ApprovalPolicy.Create(currentUser.OrganizationId!.Value, command.DocumentType, rules);
            result = created.IsSuccess ? Result.Success() : created.Error!;
            if (created.IsSuccess)
            {
                db.ApprovalPolicies.Add(created.Value);
            }
        }
        else
        {
            result = policy.ReplaceRules(rules);
        }

        if (result.IsSuccess)
        {
            await db.SaveChangesAsync(cancellationToken);
        }

        return result;
    }
}

// ---------- Approver groups (admins) ----------

public sealed record ApproverGroupDto(Guid Id, string Name, IReadOnlyList<Guid> MemberUserIds);

public sealed record GetApproverGroupsQuery : IQuery<IReadOnlyList<ApproverGroupDto>>;

internal sealed class GetApproverGroupsQueryHandler(IAppDbContext db) : IQueryHandler<GetApproverGroupsQuery, IReadOnlyList<ApproverGroupDto>>
{
    public async Task<Result<IReadOnlyList<ApproverGroupDto>>> Handle(GetApproverGroupsQuery query, CancellationToken cancellationToken)
    {
        var groups = await db.ApproverGroups.AsNoTracking().OrderBy(g => g.Name).ToListAsync(cancellationToken);
        return groups.Select(g => new ApproverGroupDto(g.Id, g.Name, g.MemberUserIds)).ToList();
    }
}

public sealed record CreateApproverGroupCommand(string Name, IReadOnlyList<Guid> MemberUserIds) : ICommand<Guid>;

internal sealed class CreateApproverGroupCommandHandler(IAppDbContext db, ICurrentUser currentUser, IOrganizationDirectory directory)
    : ICommandHandler<CreateApproverGroupCommand, Guid>
{
    public async Task<Result<Guid>> Handle(CreateApproverGroupCommand command, CancellationToken cancellationToken)
    {
        if (!await MembersAreActiveEmployees(directory, command.MemberUserIds, cancellationToken))
        {
            return ApprovalErrors.GroupMembersInvalid;
        }

        var created = ApproverGroup.Create(currentUser.OrganizationId!.Value, command.Name, command.MemberUserIds);
        if (created.IsFailure)
        {
            return created.Error!;
        }

        db.ApproverGroups.Add(created.Value);
        await db.SaveChangesAsync(cancellationToken);
        return created.Value.Id;
    }

    internal static async Task<bool> MembersAreActiveEmployees(IOrganizationDirectory directory, IReadOnlyList<Guid> members, CancellationToken cancellationToken)
    {
        var distinct = members.Distinct().ToList();
        var active = await directory.ActiveInternalUsersAsync(distinct, cancellationToken);
        return active.Count == distinct.Count;
    }
}

public sealed record ReplaceApproverGroupMembersCommand(Guid GroupId, IReadOnlyList<Guid> MemberUserIds) : ICommand;

internal sealed class ReplaceApproverGroupMembersCommandHandler(IAppDbContext db, IOrganizationDirectory directory)
    : ICommandHandler<ReplaceApproverGroupMembersCommand>
{
    public async Task<Result> Handle(ReplaceApproverGroupMembersCommand command, CancellationToken cancellationToken)
    {
        var group = await db.ApproverGroups.SingleOrDefaultAsync(g => g.Id == command.GroupId, cancellationToken);
        if (group is null)
        {
            return ApprovalErrors.GroupNotFound;
        }

        if (!await CreateApproverGroupCommandHandler.MembersAreActiveEmployees(directory, command.MemberUserIds, cancellationToken))
        {
            return ApprovalErrors.GroupMembersInvalid;
        }

        var result = group.ReplaceMembers(command.MemberUserIds);
        if (result.IsSuccess)
        {
            await db.SaveChangesAsync(cancellationToken);
        }

        return result;
    }
}

// ---------- Delegations (any employee, for their own approval rights) ----------

public sealed record DelegationDto(Guid Id, Guid FromUserId, Guid ToUserId, DateOnly ValidFrom, DateOnly ValidTo, string? Reason, bool IsRevoked);

public sealed record CreateDelegationCommand(Guid ToUserId, DateOnly ValidFrom, DateOnly ValidTo, string? Reason) : ICommand<Guid>;

internal sealed class CreateDelegationCommandValidator : AbstractValidator<CreateDelegationCommand>
{
    public CreateDelegationCommandValidator() => RuleFor(x => x.Reason).MaximumLength(200);
}

internal sealed class CreateDelegationCommandHandler(IAppDbContext db, ICurrentUser currentUser, IOrganizationDirectory directory, IClock clock)
    : ICommandHandler<CreateDelegationCommand, Guid>
{
    public async Task<Result<Guid>> Handle(CreateDelegationCommand command, CancellationToken cancellationToken)
    {
        if ((await directory.ActiveInternalUsersAsync([command.ToUserId], cancellationToken)).Count == 0)
        {
            return ApprovalErrors.DelegateNotFound;
        }

        var created = ApprovalDelegation.Create(currentUser.OrganizationId!.Value, currentUser.UserId!.Value, command.ToUserId,
            command.ValidFrom, command.ValidTo, clock.Today, command.Reason);
        if (created.IsFailure)
        {
            return created.Error!;
        }

        db.ApprovalDelegations.Add(created.Value);
        await db.SaveChangesAsync(cancellationToken);
        return created.Value.Id;
    }
}

public sealed record RevokeDelegationCommand(Guid DelegationId) : ICommand;

internal sealed class RevokeDelegationCommandHandler(IAppDbContext db, ICurrentUser currentUser) : ICommandHandler<RevokeDelegationCommand>
{
    public async Task<Result> Handle(RevokeDelegationCommand command, CancellationToken cancellationToken)
    {
        var delegation = await db.ApprovalDelegations.SingleOrDefaultAsync(d => d.Id == command.DelegationId, cancellationToken);

        // Only the person who gave the rights (or an admin) can take them back.
        if (delegation is null || (delegation.FromUserId != currentUser.UserId && !currentUser.HasRole(Role.Admin)))
        {
            return ApprovalErrors.DelegationNotFound;
        }

        delegation.Revoke();
        await db.SaveChangesAsync(cancellationToken);
        return Result.Success();
    }
}

public sealed record GetMyDelegationsQuery : IQuery<IReadOnlyList<DelegationDto>>;

internal sealed class GetMyDelegationsQueryHandler(IAppDbContext db, ICurrentUser currentUser) : IQueryHandler<GetMyDelegationsQuery, IReadOnlyList<DelegationDto>>
{
    public async Task<Result<IReadOnlyList<DelegationDto>>> Handle(GetMyDelegationsQuery query, CancellationToken cancellationToken)
    {
        var userId = currentUser.UserId;
        var list = await db.ApprovalDelegations.AsNoTracking()
            .Where(d => d.FromUserId == userId || d.ToUserId == userId)
            .OrderByDescending(d => d.ValidFrom)
            .Select(d => new DelegationDto(d.Id, d.FromUserId, d.ToUserId, d.ValidFrom, d.ValidTo, d.Reason, d.IsRevoked))
            .ToListAsync(cancellationToken);
        return list;
    }
}
