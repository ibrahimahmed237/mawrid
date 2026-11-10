using Mawrid.Domain.Common;

namespace Mawrid.Domain.Identity;

/// <summary>A buyer company. It is the tenant: all business data belongs to one organization.</summary>
public sealed class Organization : AggregateRoot<Guid>
{
    public const int NameMaxLength = 200;

    private Organization(Guid id, string name) : base(id) => Name = name;

    private Organization()
    {
        Name = null!;
    }

    public string Name { get; private set; }

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
