using FluentValidation;
using Mawrid.Application.Common.Abstractions;
using Mawrid.Application.Common.Messaging;
using Mawrid.Application.Common.Persistence;
using Mawrid.Application.Common.Security;
using Mawrid.Domain.Common;
using Mawrid.Domain.Identity;
using Microsoft.EntityFrameworkCore;

namespace Mawrid.Application.Identity.Auth;

public sealed record RefreshTokensCommand(string RefreshToken) : ICommand<TokenPairDto>;

internal sealed class RefreshTokensCommandValidator : AbstractValidator<RefreshTokensCommand>
{
    public RefreshTokensCommandValidator() => RuleFor(x => x.RefreshToken).NotEmpty().MaximumLength(200);
}

internal sealed class RefreshTokensCommandHandler(
    ITokenService tokens,
    IAppDbContext db,
    IClock clock) : ICommandHandler<RefreshTokensCommand, TokenPairDto>
{
    public async Task<Result<TokenPairDto>> Handle(RefreshTokensCommand command, CancellationToken cancellationToken)
    {
        var hash = tokens.HashRefreshToken(command.RefreshToken);
        var token = await db.RefreshTokens.SingleOrDefaultAsync(t => t.TokenHash == hash, cancellationToken);
        if (token is null)
        {
            return IdentityErrors.RefreshTokenInvalid;
        }

        var now = clock.UtcNow;
        switch (token.Check(now))
        {
            case RefreshTokenCheck.Reused:
                // A used token came back: it was copied. End every session of this login.
                await RevokeFamily.ExecuteAsync(db, token.FamilyId, now, cancellationToken);
                return IdentityErrors.RefreshTokenReused;
            case RefreshTokenCheck.Expired:
            case RefreshTokenCheck.Revoked:
                return IdentityErrors.RefreshTokenInvalid;
        }

        var user = await db.Users
            .IgnoreQueryFilters([QueryFilters.Tenant])
            .SingleOrDefaultAsync(u => u.Id == token.UserId, cancellationToken);
        if (user is null || !user.IsActive)
        {
            return IdentityErrors.RefreshTokenInvalid;
        }

        try
        {
            return await TokenIssuer.IssueAsync(user, token, tokens, db, clock, cancellationToken);
        }
        catch (DbUpdateConcurrencyException)
        {
            // Another request used this token at the same moment (for example a double click).
            return IdentityErrors.RefreshTokenInvalid;
        }
    }
}

internal static class RevokeFamily
{
    public static Task<int> ExecuteAsync(IAppDbContext db, Guid familyId, DateTimeOffset now, CancellationToken cancellationToken) =>
        db.RefreshTokens
            .Where(t => t.FamilyId == familyId && t.RevokedAtUtc == null)
            .ExecuteUpdateAsync(s => s.SetProperty(t => t.RevokedAtUtc, now), cancellationToken);
}
