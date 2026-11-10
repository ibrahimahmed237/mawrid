using FluentValidation;
using Mawrid.Application.Common.Abstractions;
using Mawrid.Application.Common.Messaging;
using Mawrid.Application.Common.Persistence;
using Mawrid.Application.Common.Security;
using Mawrid.Domain.Common;
using Microsoft.EntityFrameworkCore;

namespace Mawrid.Application.Identity.Auth;

/// <summary>Ends the session: revokes the refresh token and every token in its family. Always succeeds.</summary>
public sealed record LogoutCommand(string RefreshToken) : ICommand;

internal sealed class LogoutCommandValidator : AbstractValidator<LogoutCommand>
{
    public LogoutCommandValidator() => RuleFor(x => x.RefreshToken).NotEmpty().MaximumLength(200);
}

internal sealed class LogoutCommandHandler(ITokenService tokens, IAppDbContext db, IClock clock) : ICommandHandler<LogoutCommand>
{
    public async Task<Result> Handle(LogoutCommand command, CancellationToken cancellationToken)
    {
        var hash = tokens.HashRefreshToken(command.RefreshToken);
        var familyId = await db.RefreshTokens
            .Where(t => t.TokenHash == hash)
            .Select(t => (Guid?)t.FamilyId)
            .SingleOrDefaultAsync(cancellationToken);

        if (familyId is { } id)
        {
            await RevokeFamily.ExecuteAsync(db, id, clock.UtcNow, cancellationToken);
        }

        return Result.Success();
    }
}
