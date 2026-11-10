using Mawrid.Domain.Common;
using Mawrid.Domain.Identity;

namespace Mawrid.Application.Common.Security;

/// <summary>
/// Who is making the current request, and which organization it is for.
/// For HTTP requests this comes from the access token; background jobs set it explicitly.
/// </summary>
public interface ICurrentUser
{
    bool IsAuthenticated { get; }

    Guid? UserId { get; }

    /// <summary>The tenant. Never taken from the request body or URL (ADR-0003).</summary>
    Guid? OrganizationId { get; }

    Guid? SupplierId { get; }

    IReadOnlyList<RoleAssignment> Roles { get; }

    /// <summary>The id that ties together all logs and audit rows of one request.</summary>
    string? CorrelationId { get; }

    bool HasRole(Role role);
}
