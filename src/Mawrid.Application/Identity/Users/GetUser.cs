using Mawrid.Application.Common.Messaging;
using Mawrid.Application.Common.Persistence;
using Mawrid.Application.Common.Security;
using Mawrid.Domain.Common;
using Mawrid.Domain.Identity;
using Microsoft.EntityFrameworkCore;

namespace Mawrid.Application.Identity.Users;

/// <summary>One user of the current organization. Users of other organizations are "not found".</summary>
public sealed record GetUserQuery(Guid UserId) : IQuery<UserDto>;

internal sealed class GetUserQueryHandler(IAppDbContext db) : IQueryHandler<GetUserQuery, UserDto>
{
    public async Task<Result<UserDto>> Handle(GetUserQuery query, CancellationToken cancellationToken)
    {
        var user = await db.Users.AsNoTracking().SingleOrDefaultAsync(u => u.Id == query.UserId, cancellationToken);
        return user is null ? IdentityErrors.UserNotFound : UserMapping.ToDto(user);
    }
}

/// <summary>The logged-in user.</summary>
public sealed record GetMeQuery : IQuery<UserDto>;

internal sealed class GetMeQueryHandler(IAppDbContext db, ICurrentUser currentUser) : IQueryHandler<GetMeQuery, UserDto>
{
    public async Task<Result<UserDto>> Handle(GetMeQuery query, CancellationToken cancellationToken)
    {
        var user = await db.Users.AsNoTracking().SingleOrDefaultAsync(u => u.Id == currentUser.UserId, cancellationToken);
        return user is null ? IdentityErrors.UserNotFound : UserMapping.ToDto(user);
    }
}
