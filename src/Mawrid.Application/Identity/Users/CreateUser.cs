using FluentValidation;
using Mawrid.Application.Common.Messaging;
using Mawrid.Application.Common.Persistence;
using Mawrid.Application.Common.Security;
using Mawrid.Application.Contracts.Suppliers;
using Mawrid.Domain.Common;
using Mawrid.Domain.Identity;
using Microsoft.EntityFrameworkCore;

namespace Mawrid.Application.Identity.Users;

public sealed record CreateUserCommand(
    string Email,
    string DisplayName,
    string Password,
    IReadOnlyList<RoleAssignmentDto> Roles,
    Guid? SupplierId = null) : ICommand<Guid>;

internal sealed class CreateUserCommandValidator : AbstractValidator<CreateUserCommand>
{
    public CreateUserCommandValidator()
    {
        RuleFor(x => x.Email).NotEmpty().EmailAddress().MaximumLength(User.EmailMaxLength);
        RuleFor(x => x.DisplayName).NotEmpty().MaximumLength(User.DisplayNameMaxLength);
        RuleFor(x => x.Password).NotEmpty().MinimumLength(10).MaximumLength(200);
        RuleFor(x => x.Roles).NotEmpty();
        RuleForEach(x => x.Roles).SetValidator(new RoleAssignmentDtoValidator());
    }
}

internal sealed class CreateUserCommandHandler(
    IAppDbContext db,
    IIdentityService identity,
    ICurrentUser currentUser,
    ISupplierDirectory suppliers) : ICommandHandler<CreateUserCommand, Guid>
{
    public async Task<Result<Guid>> Handle(CreateUserCommand command, CancellationToken cancellationToken)
    {
        var organizationId = currentUser.OrganizationId
            ?? throw new InvalidOperationException("Creating a user needs an organization context.");

        var roles = command.Roles.Select(r => r.ToDomain()).ToList();
        var created = User.Create(organizationId, command.Email, command.DisplayName, roles, command.SupplierId);
        if (created.IsFailure)
        {
            return created.Error!;
        }

        var user = created.Value;

        // The email is the login name, so it must be unique across all organizations.
        var emailTaken = await db.Users.IgnoreQueryFilters([QueryFilters.Tenant]).AnyAsync(u => u.Email == user.Email, cancellationToken);
        if (emailTaken)
        {
            return IdentityErrors.EmailTaken;
        }

        if (!await UserRules.CostCentersExistAsync(db.CostCenters, roles, cancellationToken))
        {
            return IdentityErrors.CostCenterNotFound;
        }

        // Supplier users are invited for a supplier of this organization (asked through the Suppliers module).
        if (user.SupplierId is { } supplierId && await suppliers.FindAsync(supplierId, cancellationToken) is null)
        {
            return IdentityErrors.SupplierNotFound;
        }

        // Identity's UserManager saves through the same scoped DbContext, so the business user
        // and its credential are written in one SaveChanges (one transaction). If the password
        // breaks the rules, nothing is saved.
        db.Users.Add(user);
        var credential = await identity.CreateCredentialAsync(user.Id, user.Email, command.Password, cancellationToken);
        return credential.IsSuccess ? user.Id : credential.Error!;
    }
}
