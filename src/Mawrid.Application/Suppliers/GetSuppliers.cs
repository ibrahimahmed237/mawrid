using FluentValidation;
using Mawrid.Application.Common.Abstractions;
using Mawrid.Application.Common.Messaging;
using Mawrid.Application.Common.Paging;
using Mawrid.Application.Common.Persistence;
using Mawrid.Domain.Common;
using Mawrid.Domain.Suppliers;
using Microsoft.EntityFrameworkCore;

namespace Mawrid.Application.Suppliers;

public sealed record GetSuppliersQuery(PageRequest Page, string? Status = null, string? Search = null) : IQuery<PagedList<SupplierSummaryDto>>;

internal sealed class GetSuppliersQueryValidator : AbstractValidator<GetSuppliersQuery>
{
    public GetSuppliersQueryValidator()
    {
        RuleFor(x => x.Page).ValidPage();
        RuleFor(x => x.Search).MaximumLength(100);
        RuleFor(x => x.Status)
            .Must(s => s is null || Enum.TryParse<SupplierStatus>(s, ignoreCase: true, out _))
            .WithMessage($"Status must be one of: {string.Join(", ", Enum.GetNames<SupplierStatus>())}.");
    }
}

internal sealed class GetSuppliersQueryHandler(IAppDbContext db, IClock clock) : IQueryHandler<GetSuppliersQuery, PagedList<SupplierSummaryDto>>
{
    public async Task<Result<PagedList<SupplierSummaryDto>>> Handle(GetSuppliersQuery query, CancellationToken cancellationToken)
    {
        // Documents are loaded with the supplier because "awardable" depends on their expiry dates.
        var suppliers = db.Suppliers.AsNoTracking();

        if (query.Status is not null)
        {
            var status = Enum.Parse<SupplierStatus>(query.Status, ignoreCase: true);
            suppliers = suppliers.Where(s => s.Status == status);
        }

        if (!string.IsNullOrWhiteSpace(query.Search))
        {
            var search = query.Search.Trim();
            suppliers = suppliers.Where(s => s.NameEn.Contains(search) || s.NameAr.Contains(search) || ((string)(object)s.VatNumber).Contains(search));
        }

        var page = await suppliers.OrderBy(s => s.NameEn).ThenBy(s => s.Id).ToPagedListAsync(query.Page, cancellationToken);
        var today = clock.Today;

        return new PagedList<SupplierSummaryDto>(
            page.Items.Select(s => new SupplierSummaryDto(s.Id, s.NameAr, s.NameEn, s.VatNumber.Value, s.Status.ToString(), s.IsAwardable(today))).ToList(),
            page.Page, page.PageSize, page.Total);
    }
}

public sealed record GetSupplierQuery(Guid SupplierId) : IQuery<SupplierDto>;

internal sealed class GetSupplierQueryHandler(IAppDbContext db, IClock clock) : IQueryHandler<GetSupplierQuery, SupplierDto>
{
    public async Task<Result<SupplierDto>> Handle(GetSupplierQuery query, CancellationToken cancellationToken)
    {
        var supplier = await db.Suppliers.AsNoTracking().SingleOrDefaultAsync(s => s.Id == query.SupplierId, cancellationToken);
        return supplier is null ? SupplierErrors.NotFound : SupplierMapping.ToDto(supplier, clock.Today);
    }
}
