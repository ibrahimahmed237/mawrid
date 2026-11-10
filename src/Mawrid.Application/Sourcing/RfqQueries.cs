using FluentValidation;
using Mawrid.Application.Common.Abstractions;
using Mawrid.Application.Common.Messaging;
using Mawrid.Application.Common.Paging;
using Mawrid.Application.Common.Persistence;
using Mawrid.Application.Common.Security;
using Mawrid.Application.Contracts.Suppliers;
using Mawrid.Domain.Common;
using Mawrid.Domain.Sourcing;
using Microsoft.EntityFrameworkCore;

namespace Mawrid.Application.Sourcing;

public sealed record GetRfqsQuery(PageRequest Page, string? Status = null) : IQuery<PagedList<RfqSummaryDto>>;

internal sealed class GetRfqsQueryValidator : AbstractValidator<GetRfqsQuery>
{
    public GetRfqsQueryValidator()
    {
        RuleFor(x => x.Page).ValidPage();
        RuleFor(x => x.Status).Must(s => s is null || Enum.TryParse<RfqStatus>(s, ignoreCase: true, out _));
    }
}

internal sealed class GetRfqsQueryHandler(IAppDbContext db) : IQueryHandler<GetRfqsQuery, PagedList<RfqSummaryDto>>
{
    public async Task<Result<PagedList<RfqSummaryDto>>> Handle(GetRfqsQuery query, CancellationToken cancellationToken)
    {
        var rfqs = db.Rfqs.AsNoTracking();
        if (query.Status is not null)
        {
            var status = Enum.Parse<RfqStatus>(query.Status, ignoreCase: true);
            rfqs = rfqs.Where(r => r.Status == status);
        }

        var page = await rfqs.OrderByDescending(r => r.Number).ToPagedListAsync(query.Page, cancellationToken);
        return new PagedList<RfqSummaryDto>(
            page.Items.Select(r => new RfqSummaryDto(r.Id, r.Number, r.Title, r.Status.ToString(), r.Deadline, r.Invitations.Count)).ToList(),
            page.Page, page.PageSize, page.Total);
    }
}

public sealed record GetRfqQuery(Guid RfqId) : IQuery<RfqDto>;

internal sealed class GetRfqQueryHandler(IAppDbContext db, ISupplierDirectory suppliers) : IQueryHandler<GetRfqQuery, RfqDto>
{
    public async Task<Result<RfqDto>> Handle(GetRfqQuery query, CancellationToken cancellationToken)
    {
        var rfq = await db.Rfqs.AsNoTracking().SingleOrDefaultAsync(r => r.Id == query.RfqId, cancellationToken);
        if (rfq is null)
        {
            return SourcingErrors.NotFound;
        }

        var names = (await suppliers.FindManyAsync(rfq.Invitations.Select(i => i.SupplierId).ToList(), cancellationToken)).ToDictionary(s => s.Id);
        return new RfqDto(rfq.Id, rfq.Number, rfq.Title, rfq.Description, rfq.Status.ToString(), rfq.CostCenterId, rfq.Deadline, rfq.Revision,
            rfq.SingleSourceReason,
            rfq.Items.OrderBy(i => i.LineNumber).Select(i => new RfqItemDto(i.LineNumber, i.Description, i.Quantity, i.Unit, i.RequisitionId)).ToList(),
            rfq.Invitations.OrderBy(i => i.InvitedAtUtc).ThenBy(i => i.SupplierId).Select(i => new InvitedSupplierDto(i.SupplierId, names.GetValueOrDefault(i.SupplierId)?.NameEn ?? "?",
                names.GetValueOrDefault(i.SupplierId)?.NameAr ?? "?")).ToList(),
            rfq.Awards.OrderBy(a => a.ItemLineNumber).Select(a => new RfqAwardDto(a.ItemLineNumber, a.SupplierId, a.UnitPrice, a.IsLowestPrice, a.Justification)).ToList());
    }
}

/// <summary>Who has submitted, and when. Available while the RFQ is still sealed.</summary>
public sealed record GetRfqSubmissionsQuery(Guid RfqId) : IQuery<IReadOnlyList<SubmissionDto>>;

internal sealed class GetRfqSubmissionsQueryHandler(IAppDbContext db, ISupplierDirectory suppliers) : IQueryHandler<GetRfqSubmissionsQuery, IReadOnlyList<SubmissionDto>>
{
    public async Task<Result<IReadOnlyList<SubmissionDto>>> Handle(GetRfqSubmissionsQuery query, CancellationToken cancellationToken)
    {
        var rfq = await db.Rfqs.AsNoTracking().SingleOrDefaultAsync(r => r.Id == query.RfqId, cancellationToken);
        if (rfq is null)
        {
            return SourcingErrors.NotFound;
        }

        // Only metadata is selected: prices never leave the database while the RFQ is sealed.
        var submissions = await db.Quotations.AsNoTracking()
            .Where(q => q.RfqId == rfq.Id)
            .Select(q => new { q.SupplierId, q.SubmittedAtUtc, q.Version, q.RfqRevision })
            .ToListAsync(cancellationToken);
        var names = (await suppliers.FindManyAsync(submissions.Select(s => s.SupplierId).ToList(), cancellationToken)).ToDictionary(s => s.Id);

        return submissions
            .OrderBy(s => s.SubmittedAtUtc)
            .Select(s => new SubmissionDto(s.SupplierId, names.GetValueOrDefault(s.SupplierId)?.NameEn ?? "?", s.SubmittedAtUtc, s.Version, s.RfqRevision < rfq.Revision))
            .ToList();
    }
}

/// <summary>
/// Side-by-side prices. Allowed only after the deadline (ADR-0007). The first call closes the RFQ
/// if needed and records who opened the quotations and when.
/// </summary>
public sealed record GetRfqComparisonQuery(Guid RfqId) : IQuery<ComparisonDto>;

internal sealed class GetRfqComparisonQueryHandler(IAppDbContext db, ISupplierDirectory suppliers, ICurrentUser currentUser, IClock clock)
    : IQueryHandler<GetRfqComparisonQuery, ComparisonDto>
{
    public async Task<Result<ComparisonDto>> Handle(GetRfqComparisonQuery query, CancellationToken cancellationToken)
    {
        var rfq = await db.Rfqs.SingleOrDefaultAsync(r => r.Id == query.RfqId, cancellationToken);
        if (rfq is null)
        {
            return SourcingErrors.NotFound;
        }

        var now = clock.UtcNow;
        if (rfq.IsSealed(now))
        {
            return SourcingErrors.DeadlineNotPassed;
        }

        if (rfq.Status == RfqStatus.Published)
        {
            rfq.Close(now);
        }

        rfq.RecordQuotationsOpened(currentUser.UserId!.Value, now);
        await db.SaveChangesAsync(cancellationToken);

        var quotations = await db.Quotations.AsNoTracking().Where(q => q.RfqId == rfq.Id).ToListAsync(cancellationToken);
        var names = (await suppliers.FindManyAsync(quotations.Select(q => q.SupplierId).ToList(), cancellationToken)).ToDictionary(s => s.Id, s => s.NameEn);
        return Comparison.Build(rfq, quotations, names, clock.Today);
    }
}

internal static class Comparison
{
    /// <summary>A quote can be awarded if it is still valid and answers the latest RFQ revision.</summary>
    public static bool IsEligible(Quotation quotation, Rfq rfq, DateOnly today) =>
        quotation.ValidUntil >= today && !quotation.NeedsReconfirmation(rfq);

    public static ComparisonDto Build(Rfq rfq, IReadOnlyList<Quotation> quotations, IReadOnlyDictionary<Guid, string> names, DateOnly today)
    {
        var items = rfq.Items.OrderBy(i => i.LineNumber).Select(item =>
        {
            var quotes = quotations
                .SelectMany(q => q.Lines.Where(l => l.ItemLineNumber == item.LineNumber).Select(l => (Quotation: q, Line: l)))
                .ToList();
            var lowest = quotes.Where(x => IsEligible(x.Quotation, rfq, today)).Select(x => x.Line.UnitPrice).DefaultIfEmpty().Min();

            return new ComparisonItemDto(item.LineNumber, item.Description, item.Quantity, item.Unit,
                quotes.OrderBy(x => x.Line.UnitPrice)
                    .Select(x => new ComparisonQuoteDto(x.Quotation.SupplierId, names.GetValueOrDefault(x.Quotation.SupplierId, "?"), x.Quotation.Id,
                        x.Line.UnitPrice, x.Line.VatCategory.ToString(), x.Line.LeadTimeDays, Money.Net(item.Quantity, x.Line.UnitPrice),
                        IsLowest: IsEligible(x.Quotation, rfq, today) && x.Line.UnitPrice == lowest,
                        IsEligible: IsEligible(x.Quotation, rfq, today)))
                    .ToList());
        }).ToList();

        var suppliers = quotations.Select(q => new SupplierTotalDto(q.SupplierId, names.GetValueOrDefault(q.SupplierId, "?"),
                q.Lines.Sum(l => Money.Net(rfq.Items.Single(i => i.LineNumber == l.ItemLineNumber).Quantity, l.UnitPrice)),
                q.Lines.Count, q.ValidUntil, q.NeedsReconfirmation(rfq)))
            .OrderBy(s => s.NetTotalOfQuotedItems)
            .ToList();

        return new ComparisonDto(rfq.Id, rfq.Number, items, suppliers);
    }
}
