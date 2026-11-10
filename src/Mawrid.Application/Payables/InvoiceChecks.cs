using Mawrid.Application.Common.Abstractions;
using Mawrid.Application.Common.Persistence;
using Mawrid.Application.Contracts.Identity;
using Mawrid.Application.Contracts.Purchasing;
using Mawrid.Application.Contracts.Suppliers;
using Mawrid.Domain.Payables;
using Microsoft.EntityFrameworkCore;

namespace Mawrid.Application.Payables;

/// <summary>Loads what the three-way match needs and stores the result on the invoice (the caller saves).</summary>
internal sealed class InvoiceChecks(
    IAppDbContext db, IPurchaseOrderInvoicing purchaseOrders, ISupplierDirectory suppliers, IOrganizationDirectory organization, IClock clock)
{
    public async Task RunAsync(SupplierInvoice invoice, PurchaseOrderForInvoice? order, CancellationToken cancellationToken)
    {
        order ??= await purchaseOrders.FindAsync(invoice.PurchaseOrderId, cancellationToken);
        var supplier = await suppliers.FindAsync(invoice.SupplierId, cancellationToken);
        var settings = await organization.GetSettingsAsync(cancellationToken);

        // Quantities on this supplier's other invoices for the same PO count as already invoiced.
        var otherQuantities = await db.SupplierInvoices.AsNoTracking()
            .Where(i => i.PurchaseOrderId == invoice.PurchaseOrderId && i.Id != invoice.Id && i.Status != InvoiceStatus.Rejected)
            .SelectMany(i => i.Lines)
            .GroupBy(l => l.PoLineNumber)
            .Select(g => new { LineNumber = g.Key, Quantity = g.Sum(l => l.Quantity) })
            .ToDictionaryAsync(x => x.LineNumber, x => x.Quantity, cancellationToken);

        var context = new MatchContext(
            supplier?.VatNumber ?? string.Empty,
            supplier?.HasPendingBankChange ?? false,
            settings.PriceTolerancePercent,
            settings.PriceToleranceMaxAmount,
            (order?.Lines ?? []).Select(l => new PoLineForMatch(l.LineNumber, l.UnitPrice, l.AcceptedQuantity, otherQuantities.GetValueOrDefault(l.LineNumber)))
                .ToList());

        invoice.ApplyCheck(InvoiceChecker.Check(invoice, context), clock.UtcNow);
    }
}
