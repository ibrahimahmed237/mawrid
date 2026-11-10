using FluentValidation;
using Mawrid.Application.Common.Messaging;
using Mawrid.Application.Common.Paging;
using Mawrid.Application.Common.Persistence;
using Mawrid.Application.Common.Security;
using Mawrid.Domain.Common;
using Mawrid.Domain.Requisitions;
using Microsoft.EntityFrameworkCore;

namespace Mawrid.Application.Requisitions;

public sealed record GetRequisitionsQuery(PageRequest Page, string? Status = null) : IQuery<PagedList<RequisitionSummaryDto>>;

internal sealed class GetRequisitionsQueryValidator : AbstractValidator<GetRequisitionsQuery>
{
    public GetRequisitionsQueryValidator()
    {
        RuleFor(x => x.Page).ValidPage();
        RuleFor(x => x.Status).Must(s => s is null || Enum.TryParse<RequisitionStatus>(s, ignoreCase: true, out _))
            .WithMessage($"Status must be one of: {string.Join(", ", Enum.GetNames<RequisitionStatus>())}.");
    }
}

internal sealed class GetRequisitionsQueryHandler(IAppDbContext db, ICurrentUser currentUser)
    : IQueryHandler<GetRequisitionsQuery, PagedList<RequisitionSummaryDto>>
{
    public async Task<Result<PagedList<RequisitionSummaryDto>>> Handle(GetRequisitionsQuery query, CancellationToken cancellationToken)
    {
        var requisitions = RequisitionAccess.Visible(db.Requisitions.AsNoTracking(), currentUser);
        if (query.Status is not null)
        {
            var status = Enum.Parse<RequisitionStatus>(query.Status, ignoreCase: true);
            requisitions = requisitions.Where(r => r.Status == status);
        }

        var page = await requisitions.OrderByDescending(r => r.Number).ToPagedListAsync(query.Page, cancellationToken);
        return new PagedList<RequisitionSummaryDto>(
            page.Items.Select(r => new RequisitionSummaryDto(r.Id, r.Number, r.Title, r.Status.ToString(), r.EstimatedNetTotal, r.NeededBy, r.RequesterId)).ToList(),
            page.Page, page.PageSize, page.Total);
    }
}

public sealed record GetRequisitionQuery(Guid RequisitionId) : IQuery<RequisitionDto>;

internal sealed class GetRequisitionQueryHandler(IAppDbContext db, ICurrentUser currentUser) : IQueryHandler<GetRequisitionQuery, RequisitionDto>
{
    public async Task<Result<RequisitionDto>> Handle(GetRequisitionQuery query, CancellationToken cancellationToken)
    {
        var requisition = await RequisitionAccess.Visible(db.Requisitions.AsNoTracking(), currentUser)
            .SingleOrDefaultAsync(r => r.Id == query.RequisitionId, cancellationToken);
        return requisition is null ? RequisitionErrors.NotFound : RequisitionMapping.ToDto(requisition);
    }
}
