using Mawrid.Domain.Common;

namespace Mawrid.Domain.Identity;

/// <summary>A buyer company. It is the tenant: all business data belongs to one organization.</summary>
public sealed class Organization : AggregateRoot<Guid>
{
    public const int NameMaxLength = 200;

    private Organization(Guid id, string name) : base(id)
    {
        Name = name;
        Settings = ProcurementSettings.Default;
    }

    private Organization()
    {
        Name = null!;
        Settings = null!;
    }

    public string Name { get; private set; }

    /// <summary>Limits and tolerances the procurement rules use. Admins can change them.</summary>
    public ProcurementSettings Settings { get; private set; }

    public Result UpdateSettings(ProcurementSettings settings)
    {
        var check = settings.Validate();
        if (check.IsFailure)
        {
            return check;
        }

        Settings = settings;
        return Result.Success();
    }

    public static Result<Organization> Create(string name)
    {
        name = name?.Trim() ?? string.Empty;
        if (name.Length is < 2 or > NameMaxLength)
        {
            return IdentityErrors.OrganizationNameInvalid;
        }

        var organization = new Organization(Guid.CreateVersion7(), name);
        organization.Raise(new OrganizationCreated(organization.Id));
        return organization;
    }
}

public sealed record OrganizationCreated(Guid OrganizationId) : IDomainEvent;
