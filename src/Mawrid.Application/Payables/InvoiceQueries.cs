using FluentValidation;
using Mawrid.Application.Common.Messaging;
using Mawrid.Application.Common.Paging;
using Mawrid.Application.Common.Persistence;
using Mawrid.Application.Common.Security;
using Mawrid.Domain.Common;
using Mawrid.Domain.Payables;
using Microsoft.EntityFrameworkCore;

namespace Mawrid.Application.Payables;

public sealed record InvoiceSummaryDto(
    Guid Id, string SupplierInvoiceNumber, Guid SupplierId, Guid PurchaseOrderId, DateOnly IssueDate, decimal Total, string Status, int OpenHolds);

public sealed record InvoiceDto(
    Guid Id,
    string SupplierInvoiceNumber,
    Guid SupplierId,
    Guid PurchaseOrderId,
    DateOnly IssueDate,
    string Source,
    Guid? XmlFileId,
    Guid? PdfFileId,
    string? QrCode,
    decimal NetTotal,
    decimal VatTotal,
    decimal Total,
    string Status,
    DateTimeOffset ReceivedAtUtc,
    DateTimeOffset? CheckedAtUtc,
    DateTimeOffset? ReleasedAtUtc,
    string? RejectionReason,
    IReadOnlyList<InvoiceLineDto> Lines,
    IReadOnlyList<HoldDto> Holds);

public sealed record HoldDto(string Code, int? LineNumber, string Expected, string Actual, bool IsOverridden, string? OverrideReason, Guid? OverriddenBy);

/// <summary>What a supplier sees: status and outcome, not the internal hold details.</summary>
public sealed record SupplierInvoiceDto(
    Guid Id, string SupplierInvoiceNumber, Guid PurchaseOrderId, DateOnly IssueDate, decimal Total, string Status,
    DateTimeOffset ReceivedAtUtc, DateTimeOffset? ReleasedAtUtc, string? RejectionReason);

internal static class InvoiceMapping
{
    public static InvoiceDto ToDto(SupplierInvoice i) => new(
        i.Id, i.SupplierInvoiceNumber, i.SupplierId, i.PurchaseOrderId, i.IssueDate, i.Source.ToString(), i.XmlFileId, i.PdfFileId, i.QrCode,
        i.NetTotal, i.VatTotal, i.Total, i.Status.ToString(), i.ReceivedAtUtc, i.CheckedAtUtc, i.ReleasedAtUtc, i.RejectionReason,
        i.Lines.OrderBy(l => l.PoLineNumber).Select(l => new InvoiceLineDto(l.PoLineNumber, l.Description, l.Quantity, l.UnitPrice,
            l.VatCategory.ToString(), l.NetAmount, l.VatAmount)).ToList(),
        i.Holds.OrderBy(h => h.Code).ThenBy(h => h.LineNumber).Select(h => new HoldDto(h.Code, h.LineNumber, h.Expected, h.Actual,
            h.IsOverridden, h.OverrideReason, h.OverriddenBy)).ToList());

    public static SupplierInvoiceDto ToSupplierDto(SupplierInvoice i) => new(
        i.Id, i.SupplierInvoiceNumber, i.PurchaseOrderId, i.IssueDate, i.Total,
        // "OnHold" and "Matched" both mean "being processed" to the supplier.
        i.Status is InvoiceStatus.OnHold or InvoiceStatus.Matched ? "InReview" : i.Status.ToString(),
        i.ReceivedAtUtc, i.ReleasedAtUtc, i.RejectionReason);
}

/// <summary>The AP work list, for example <c>?status=OnHold</c>.</summary>
public sealed record GetInvoicesQuery(PageRequest Page, string? Status = null, Guid? PurchaseOrderId = null) : IQuery<PagedList<InvoiceSummaryDto>>;

internal sealed class GetInvoicesQueryValidator : AbstractValidator<GetInvoicesQuery>
{
    public GetInvoicesQueryValidator()
    {
        RuleFor(x => x.Page).ValidPage();
        RuleFor(x => x.Status).Must(s => s is null || Enum.TryParse<InvoiceStatus>(s, ignoreCase: true, out _));
    }
}

internal sealed class GetInvoicesQueryHandler(IAppDbContext db) : IQueryHandler<GetInvoicesQuery, PagedList<InvoiceSummaryDto>>
{
    public async Task<Result<PagedList<InvoiceSummaryDto>>> Handle(GetInvoicesQuery query, CancellationToken cancellationToken)
    {
        var invoices = db.SupplierInvoices.AsNoTracking();
        if (query.Status is not null)
        {
            var status = Enum.Parse<InvoiceStatus>(query.Status, ignoreCase: true);
            invoices = invoices.Where(i => i.Status == status);
        }

        if (query.PurchaseOrderId is { } poId)
        {
            invoices = invoices.Where(i => i.PurchaseOrderId == poId);
        }

        var page = await invoices.OrderBy(i => i.ReceivedAtUtc).ToPagedListAsync(query.Page, cancellationToken);
        return new PagedList<InvoiceSummaryDto>(
            page.Items.Select(i => new InvoiceSummaryDto(i.Id, i.SupplierInvoiceNumber, i.SupplierId, i.PurchaseOrderId, i.IssueDate, i.Total,
                i.Status.ToString(), i.OpenHolds.Count())).ToList(),
            page.Page, page.PageSize, page.Total);
    }
}

public sealed record GetInvoiceQuery(Guid InvoiceId) : IQuery<InvoiceDto>;

internal sealed class GetInvoiceQueryHandler(IAppDbContext db) : IQueryHandler<GetInvoiceQuery, InvoiceDto>
{
    public async Task<Result<InvoiceDto>> Handle(GetInvoiceQuery query, CancellationToken cancellationToken)
    {
        var invoice = await db.SupplierInvoices.AsNoTracking().SingleOrDefaultAsync(i => i.Id == query.InvoiceId, cancellationToken);
        return invoice is null ? PayablesErrors.NotFound : InvoiceMapping.ToDto(invoice);
    }
}

public sealed record GetMyInvoicesQuery : IQuery<IReadOnlyList<SupplierInvoiceDto>>;

internal sealed class GetMyInvoicesQueryHandler(IAppDbContext db, ICurrentUser currentUser) : IQueryHandler<GetMyInvoicesQuery, IReadOnlyList<SupplierInvoiceDto>>
{
    public async Task<Result<IReadOnlyList<SupplierInvoiceDto>>> Handle(GetMyInvoicesQuery query, CancellationToken cancellationToken)
    {
        var invoices = await db.SupplierInvoices.AsNoTracking()
            .Where(i => i.SupplierId == currentUser.SupplierId)
            .OrderByDescending(i => i.ReceivedAtUtc)
            .Take(200)
            .ToListAsync(cancellationToken);
        return invoices.Select(InvoiceMapping.ToSupplierDto).ToList();
    }
}

public sealed record GetMyInvoiceQuery(Guid InvoiceId) : IQuery<SupplierInvoiceDto>;

internal sealed class GetMyInvoiceQueryHandler(IAppDbContext db, ICurrentUser currentUser) : IQueryHandler<GetMyInvoiceQuery, SupplierInvoiceDto>
{
    public async Task<Result<SupplierInvoiceDto>> Handle(GetMyInvoiceQuery query, CancellationToken cancellationToken)
    {
        var invoice = await db.SupplierInvoices.AsNoTracking()
            .SingleOrDefaultAsync(i => i.Id == query.InvoiceId && i.SupplierId == currentUser.SupplierId, cancellationToken);
        return invoice is null ? PayablesErrors.NotFound : InvoiceMapping.ToSupplierDto(invoice);
    }
}
