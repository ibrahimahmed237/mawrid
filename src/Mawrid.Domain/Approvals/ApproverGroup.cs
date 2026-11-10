using Mawrid.Domain.Common;

namespace Mawrid.Domain.Approvals;

/// <summary>A named list of people who can decide a step, such as "Department directors" or "CFO".</summary>
public sealed class ApproverGroup : AggregateRoot<Guid>, ITenantOwned
{
    public const int NameMaxLength = 100;
    public const int MaxMembers = 50;

    private ApproverGroup(Guid id, Guid organizationId, string name, List<Guid> members) : base(id)
    {
        OrganizationId = organizationId;
        Name = name;
        MemberUserIds = members;
    }

    private ApproverGroup()
    {
        Name = null!;
        MemberUserIds = [];
    }

    public Guid OrganizationId { get; private set; }

    public string Name { get; private set; }

    /// <summary>Stored as a JSON array column (an EF Core primitive collection).</summary>
    public List<Guid> MemberUserIds { get; private set; }

    public static Result<ApproverGroup> Create(Guid organizationId, string name, IReadOnlyCollection<Guid> members)
    {
        name = name?.Trim() ?? string.Empty;
        if (name.Length is < 2 or > NameMaxLength)
        {
            return ApprovalErrors.GroupNameInvalid;
        }

        var group = new ApproverGroup(Guid.CreateVersion7(), organizationId, name, []);
        var result = group.ReplaceMembers(members);
        return result.IsSuccess ? group : result.Error!;
    }

    public Result ReplaceMembers(IReadOnlyCollection<Guid> members)
    {
        var distinct = members.Distinct().ToList();
        if (distinct.Count is 0 or > MaxMembers)
        {
            return ApprovalErrors.GroupMemberCount;
        }

        MemberUserIds = distinct;
        return Result.Success();
    }
}
