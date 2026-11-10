using System.Text.RegularExpressions;
using Mawrid.Domain.Common;

namespace Mawrid.Domain.Identity;

/// <summary>A department or budget owner that spend is charged to, such as "IT-001".</summary>
public sealed partial class CostCenter : AggregateRoot<Guid>, ITenantOwned
{
    public const int CodeMaxLength = 20;
    public const int NameMaxLength = 200;

    private CostCenter(Guid id, Guid organizationId, string code, string name) : base(id)
    {
        OrganizationId = organizationId;
        Code = code;
        Name = name;
        IsActive = true;
    }

    private CostCenter()
    {
        Code = null!;
        Name = null!;
    }

    public Guid OrganizationId { get; private set; }

    /// <summary>Short unique code, upper case, such as <c>IT-001</c>. Unique per organization.</summary>
    public string Code { get; private set; }

    public string Name { get; private set; }

    public bool IsActive { get; private set; }

    public static Result<CostCenter> Create(Guid organizationId, string code, string name)
    {
        code = code?.Trim().ToUpperInvariant() ?? string.Empty;
        name = name?.Trim() ?? string.Empty;

        if (!CodePattern().IsMatch(code))
        {
            return IdentityErrors.CostCenterCodeInvalid;
        }

        if (name.Length is < 2 or > NameMaxLength)
        {
            return IdentityErrors.CostCenterNameInvalid;
        }

        return new CostCenter(Guid.CreateVersion7(), organizationId, code, name);
    }

    public void Deactivate() => IsActive = false;

    [GeneratedRegex("^[A-Z0-9][A-Z0-9-]{1,19}$")]
    private static partial Regex CodePattern();
}
