using FluentValidation;
using Mawrid.Application.Common.Abstractions;
using Mawrid.Application.Common.Messaging;
using Mawrid.Application.Common.Persistence;
using Mawrid.Application.Common.Security;
using Mawrid.Domain.Common;
using Mawrid.Domain.Identity;
using Microsoft.EntityFrameworkCore;

namespace Mawrid.Application.Identity.Auth;

public sealed record LoginCommand(string Email, string Password) : ICommand<TokenPairDto>;

internal sealed class LoginCommandValidator : AbstractValidator<LoginCommand>
{
    public LoginCommandValidator()
    {
        RuleFor(x => x.Email).NotEmpty().MaximumLength(User.EmailMaxLength);
        RuleFor(x => x.Password).NotEmpty().MaximumLength(200);
    }
}

internal sealed class LoginCommandHandler(
    IIdentityService identity,
    ITokenService tokens,
    IAppDbContext db,
    IClock clock) : ICommandHandler<LoginCommand, TokenPairDto>
{
    public async Task<Result<TokenPairDto>> Handle(LoginCommand command, CancellationToken cancellationToken)
    {
        var check = await identity.CheckPasswordAsync(command.Email, command.Password, cancellationToken);
        if (check.IsFailure)
        {
            return check.Error!;
        }

        // Nobody is logged in yet, so there is no tenant: look the user up across organizations.
        var user = await db.Users
            .IgnoreQueryFilters([QueryFilters.Tenant])
            .SingleOrDefaultAsync(u => u.Id == check.Value, cancellationToken);

        // A deactivated user gets the same answer as a wrong password, so we do not reveal who exists.
        if (user is null || !user.IsActive)
        {
            return IdentityErrors.InvalidCredentials;
        }

        return await TokenIssuer.IssueAsync(user, family: null, tokens, db, clock, cancellationToken);
    }
}
