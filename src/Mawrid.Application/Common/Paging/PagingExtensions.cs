using FluentValidation;
using Microsoft.EntityFrameworkCore;

namespace Mawrid.Application.Common.Paging;

public static class PagingExtensions
{
    public static async Task<PagedList<T>> ToPagedListAsync<T>(
        this IQueryable<T> query, PageRequest page, CancellationToken cancellationToken)
    {
        var total = await query.CountAsync(cancellationToken);
        var items = await query.Skip(page.Skip).Take(page.PageSize).ToListAsync(cancellationToken);
        return new PagedList<T>(items, page.Page, page.PageSize, total);
    }

    /// <summary>Shared validation rules for paging, used by list query validators.</summary>
    public static void ValidPage<T>(this IRuleBuilder<T, PageRequest> rule) =>
        rule.ChildRules(page =>
        {
            page.RuleFor(p => p.Page).GreaterThanOrEqualTo(1);
            page.RuleFor(p => p.PageSize).InclusiveBetween(1, PageRequest.MaxPageSize);
        });
}
