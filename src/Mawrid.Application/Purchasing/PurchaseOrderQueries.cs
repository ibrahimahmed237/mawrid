using FluentValidation;
using Mawrid.Application.Common.Messaging;
using Mawrid.Application.Common.Paging;
using Mawrid.Application.Common.Persistence;
using Mawrid.Application.Common.Security;
using Mawrid.Domain.Common;
using Mawrid.Domain.Purchasing;
using Microsoft.EntityFrameworkCore;

namespace Mawrid.Application.Purchasing;

public sealed record PurchaseOrderSummaryDto(Guid Id, string Number, Guid SupplierId, string Status, decimal Total, DateTimeOffset? SentAtUtc);

public sealed record PurchaseOrderDto(
    Guid Id,
    string Number,
    Guid SupplierId,
    Guid CostCenterId,
    string Source,
    Guid SourceId,
    IReadOnlyList<Guid> RequisitionIds,
    string Status,
    Guid? ApprovalRequestId,
    string? RejectionReason,
    int PaymentTermsDays,
    decimal NetTotal,
    decimal VatTotal,
    decimal Total,
    DateTimeOffset? SentAtUtc,
    DateTimeOffset? AcknowledgedAtUtc,
    IReadOnlyList<PurchaseOrderLineDto> Lines,
    IReadOnlyList<GoodsReceiptDto> Receipts);

public sealed record PurchaseOrderLineDto(
    int LineNumber, string Description, decimal Quantity, string Unit, decimal UnitPrice, string VatCategory, decimal VatRate,
    decimal NetAmount, decimal VatAmount, decimal AcceptedQuantity, decimal RejectedQuantity, decimal InvoicedQuantity);

public sealed record GoodsReceiptDto(Guid Id, Guid ReceivedBy, DateTimeOffset ReceivedAtUtc, string? Note, IReadOnlyList<GoodsReceiptLine> Lines);

internal static class PurchaseOrderMapping
{
    public static PurchaseOrderDto ToDto(PurchaseOrder po) => new(
        po.Id, po.Number, po.SupplierId, po.CostCenterId, po.Source.ToString(), po.SourceId, po.RequisitionIds, po.Status.ToString(),
        po.ApprovalRequestId, po.RejectionReason, po.PaymentTermsDays, po.NetTotal, po.VatTotal, po.Total, po.SentAtUtc, po.AcknowledgedAtUtc,
        po.Lines.OrderBy(l => l.LineNumber).Select(l => new PurchaseOrderLineDto(l.LineNumber, l.Description, l.Quantity, l.Unit, l.UnitPrice,
            l.VatCategory.ToString(), l.VatRate, l.NetAmount, l.VatAmount, l.AcceptedQuantity, l.RejectedQuantity, l.InvoicedQuantity)).ToList(),
        po.Receipts.OrderBy(r => r.ReceivedAtUtc).Select(r => new GoodsReceiptDto(r.Id, r.ReceivedBy, r.ReceivedAtUtc, r.Note, r.Lines)).ToList());
}

internal static class PurchaseOrderAccess
{
    /// <summary>Employees see all POs of the organization. Supplier users see only their company's POs, and only once sent.</summary>
    public static IQueryable<PurchaseOrder> Visible(IAppDbContext db, ICurrentUser user)
    {
        var orders = db.PurchaseOrders.AsNoTracking();
        return user.SupplierId is { } supplierId
            ? orders.Where(o => o.SupplierId == supplierId && o.SentAtUtc != null)
            : orders;
    }
}

public sealed record GetPurchaseOrdersQuery(PageRequest Page, string? Status = null) : IQuery<PagedList<PurchaseOrderSummaryDto>>;

internal sealed class GetPurchaseOrdersQueryValidator : AbstractValidator<GetPurchaseOrdersQuery>
{
    public GetPurchaseOrdersQueryValidator()
    {
        RuleFor(x => x.Page).ValidPage();
        RuleFor(x => x.Status).Must(s => s is null || Enum.TryParse<PurchaseOrderStatus>(s, ignoreCase: true, out _));
    }
}

internal sealed class GetPurchaseOrdersQueryHandler(IAppDbContext db, ICurrentUser currentUser) : IQueryHandler<GetPurchaseOrdersQuery, PagedList<PurchaseOrderSummaryDto>>
{
    public async Task<Result<PagedList<PurchaseOrderSummaryDto>>> Handle(GetPurchaseOrdersQuery query, CancellationToken cancellationToken)
    {
        var orders = PurchaseOrderAccess.Visible(db, currentUser);
        if (query.Status is not null)
        {
            var status = Enum.Parse<PurchaseOrderStatus>(query.Status, ignoreCase: true);
            orders = orders.Where(o => o.Status == status);
        }

        var page = await orders.OrderByDescending(o => o.Number).ToPagedListAsync(query.Page, cancellationToken);
        return new PagedList<PurchaseOrderSummaryDto>(
            page.Items.Select(o => new PurchaseOrderSummaryDto(o.Id, o.Number, o.SupplierId, o.Status.ToString(), o.Total, o.SentAtUtc)).ToList(),
            page.Page, page.PageSize, page.Total);
    }
}

public sealed record GetPurchaseOrderQuery(Guid PurchaseOrderId) : IQuery<PurchaseOrderDto>;

internal sealed class GetPurchaseOrderQueryHandler(IAppDbContext db, ICurrentUser currentUser) : IQueryHandler<GetPurchaseOrderQuery, PurchaseOrderDto>
{
    public async Task<Result<PurchaseOrderDto>> Handle(GetPurchaseOrderQuery query, CancellationToken cancellationToken)
    {
        var order = await PurchaseOrderAccess.Visible(db, currentUser).SingleOrDefaultAsync(o => o.Id == query.PurchaseOrderId, cancellationToken);
        return order is null ? PurchasingErrors.NotFound : PurchaseOrderMapping.ToDto(order);
    }
}
