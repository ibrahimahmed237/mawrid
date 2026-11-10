using FluentValidation;
using Mawrid.Application.Common.Messaging;
using Mawrid.Application.Common.Paging;
using Mawrid.Application.Common.Persistence;
using Mawrid.Domain.Common;
using Microsoft.EntityFrameworkCore;

namespace Mawrid.Application.Identity.Users;

public sealed record GetUsersQuery(PageRequest Page, string? Search = null) : IQuery<PagedList<UserDto>>;

internal sealed class GetUsersQueryValidator : AbstractValidator<GetUsersQuery>
{
    public GetUsersQueryValidator()
    {
        RuleFor(x => x.Page).ValidPage();
        RuleFor(x => x.Search).MaximumLength(100);
    }
}

internal sealed class GetUsersQueryHandler(IAppDbContext db) : IQueryHandler<GetUsersQuery, PagedList<UserDto>>
{
    public async Task<Result<PagedList<UserDto>>> Handle(GetUsersQuery query, CancellationToken cancellationToken)
    {
        var users = db.Users.AsNoTracking();

        if (!string.IsNullOrWhiteSpace(query.Search))
        {
            var search = query.Search.Trim();
            users = users.Where(u => u.Email.Contains(search) || u.DisplayName.Contains(search));
        }

        var page = await users
            .OrderBy(u => u.DisplayName)
            .ThenBy(u => u.Id)
            .ToPagedListAsync(query.Page, cancellationToken);

        return new PagedList<UserDto>(page.Items.Select(UserMapping.ToDto).ToList(), page.Page, page.PageSize, page.Total);
    }
}
