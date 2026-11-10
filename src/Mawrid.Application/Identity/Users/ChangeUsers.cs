using FluentValidation;
using Mawrid.Application.Common.Abstractions;
using Mawrid.Application.Common.Messaging;
using Mawrid.Application.Common.Persistence;
using Mawrid.Application.Common.Security;
using Mawrid.Domain.Common;
using Mawrid.Domain.Identity;
using Microsoft.EntityFrameworkCore;

namespace Mawrid.Application.Identity.Users;

public sealed record ReplaceUserRolesCommand(Guid UserId, IReadOnlyList<RoleAssignmentDto> Roles) : ICommand;

internal sealed class ReplaceUserRolesCommandValidator : AbstractValidator<ReplaceUserRolesCommand>
{
    public ReplaceUserRolesCommandValidator()
    {
        RuleFor(x => x.Roles).NotEmpty();
        RuleForEach(x => x.Roles).SetValidator(new RoleAssignmentDtoValidator());
    }
}

internal sealed class ReplaceUserRolesCommandHandler(IAppDbContext db, ICurrentUser currentUser)
    : ICommandHandler<ReplaceUserRolesCommand>
{
    public async Task<Result> Handle(ReplaceUserRolesCommand command, CancellationToken cancellationToken)
    {
        var user = await db.Users.SingleOrDefaultAsync(u => u.Id == command.UserId, cancellationToken);
        if (user is null)
        {
            return IdentityErrors.UserNotFound;
        }

        var roles = command.Roles.Select(r => r.ToDomain()).ToList();

        // An admin cannot remove their own Admin role; otherwise the organization could lose its last admin by mistake.
        if (user.Id == currentUser.UserId && roles.All(r => r.Role != Role.Admin))
        {
            return UserErrors.CannotRemoveOwnAdminRole;
        }

        if (!await UserRules.CostCentersExistAsync(db.CostCenters, roles, cancellationToken))
        {
            return IdentityErrors.CostCenterNotFound;
        }

        var result = user.ReplaceRoles(roles);
        if (result.IsSuccess)
        {
            await db.SaveChangesAsync(cancellationToken);
        }

        return result;
    }
}

public sealed record DeactivateUserCommand(Guid UserId) : ICommand;

internal sealed class DeactivateUserCommandHandler(IAppDbContext db, ICurrentUser currentUser, IClock clock)
    : ICommandHandler<DeactivateUserCommand>
{
    public async Task<Result> Handle(DeactivateUserCommand command, CancellationToken cancellationToken)
    {
        var user = await db.Users.SingleOrDefaultAsync(u => u.Id == command.UserId, cancellationToken);
        if (user is null)
        {
            return IdentityErrors.UserNotFound;
        }

        if (user.Id == currentUser.UserId)
        {
            return UserErrors.CannotDeactivateSelf;
        }

        user.Deactivate();

        // End all sessions in the same transaction. Access tokens already issued stay valid
        // until they expire (at most 15 minutes).
        var now = clock.UtcNow;
        var sessions = await db.RefreshTokens.Where(t => t.UserId == user.Id && t.RevokedAtUtc == null).ToListAsync(cancellationToken);
        sessions.ForEach(t => t.Revoke(now));

        await db.SaveChangesAsync(cancellationToken);
        return Result.Success();
    }
}

internal static class UserErrors
{
    public static readonly Error CannotRemoveOwnAdminRole =
        Error.RuleViolation("user.cannot_remove_own_admin", "You cannot remove your own Admin role.");

    public static readonly Error CannotDeactivateSelf =
        Error.RuleViolation("user.cannot_deactivate_self", "You cannot deactivate your own account.");
}
