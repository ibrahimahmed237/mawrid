using FluentValidation;
using Mawrid.Application.Common.Abstractions;
using Mawrid.Application.Common.Messaging;
using Mawrid.Application.Common.Persistence;
using Mawrid.Application.Contracts.Purchasing;
using Mawrid.Application.Contracts.Requisitions;
using Mawrid.Application.Contracts.Suppliers;
using Mawrid.Domain.Common;
using Mawrid.Domain.Sourcing;
using Microsoft.EntityFrameworkCore;

namespace Mawrid.Application.Sourcing;

public sealed record AwardLineDto(int ItemLineNumber, Guid SupplierId, string? Justification = null);

/// <summary>
/// Awards RFQ items to suppliers and creates one draft purchase order per winning supplier.
/// RFQ, purchase orders and requisitions are saved in one transaction.
/// </summary>
public sealed record AwardRfqCommand(Guid RfqId, IReadOnlyList<AwardLineDto> Lines) : ICommand<IReadOnlyList<Guid>>;

internal sealed class AwardRfqCommandValidator : AbstractValidator<AwardRfqCommand>
{
    public AwardRfqCommandValidator() => RuleFor(x => x.Lines).NotEmpty();
}

internal sealed class AwardRfqCommandHandler(
    IAppDbContext db,
    ISupplierDirectory suppliers,
    IPurchaseOrderDrafts purchaseOrders,
    IRequisitionSourcing requisitions,
    IClock clock) : ICommandHandler<AwardRfqCommand, IReadOnlyList<Guid>>
{
    public async Task<Result<IReadOnlyList<Guid>>> Handle(AwardRfqCommand command, CancellationToken cancellationToken)
    {
        var rfq = await db.Rfqs.SingleOrDefaultAsync(r => r.Id == command.RfqId, cancellationToken);
        if (rfq is null)
        {
            return SourcingErrors.NotFound;
        }

        var now = clock.UtcNow;
        var today = clock.Today;
        if (rfq.IsSealed(now))
        {
            return SourcingErrors.DeadlineNotPassed;
        }

        var quotations = await db.Quotations.AsNoTracking().Where(q => q.RfqId == rfq.Id).ToListAsync(cancellationToken);
        var winners = await suppliers.FindManyAsync(command.Lines.Select(l => l.SupplierId).Distinct().ToList(), cancellationToken);
        if (winners.Any(w => !w.IsAwardable))
        {
            return SourcingErrors.SupplierNotAwardable;
        }

        var decisions = new List<AwardDecision>();
        foreach (var line in command.Lines)
        {
            var quotation = quotations.SingleOrDefault(q => q.SupplierId == line.SupplierId);
            var quoteLine = quotation?.Lines.SingleOrDefault(l => l.ItemLineNumber == line.ItemLineNumber);
            if (quotation is null || quoteLine is null || !Comparison.IsEligible(quotation, rfq, today))
            {
                return SourcingErrors.AwardInvalid;
            }

            var lowest = quotations
                .Where(q => Comparison.IsEligible(q, rfq, today))
                .SelectMany(q => q.Lines.Where(l => l.ItemLineNumber == line.ItemLineNumber))
                .Min(l => l.UnitPrice);

            decisions.Add(new AwardDecision(line.ItemLineNumber, line.SupplierId, quotation.Id, quoteLine.UnitPrice, quoteLine.VatCategory,
                quoteLine.UnitPrice <= lowest, line.Justification));
        }

        var awarded = rfq.Award(decisions, now);
        if (awarded.IsFailure)
        {
            return awarded.Error!;
        }

        var orderIds = new List<Guid>();
        foreach (var group in decisions.GroupBy(d => d.SupplierId))
        {
            var items = group.Select(d => (Decision: d, Item: rfq.Items.Single(i => i.LineNumber == d.ItemLineNumber))).ToList();
            var draft = new PurchaseOrderDraft(
                group.Key,
                rfq.CostCenterId,
                PurchaseOrderSources.Rfq,
                rfq.Id,
                items.Where(x => x.Item.RequisitionId is not null).Select(x => x.Item.RequisitionId!.Value).Distinct().ToList(),
                items.Select(x => new PurchaseOrderDraftLine(x.Item.Description, x.Item.Quantity, x.Item.Unit, x.Decision.UnitPrice, x.Decision.VatCategory)).ToList());

            var order = await purchaseOrders.CreateDraftAsync(draft, cancellationToken);
            if (order.IsFailure)
            {
                return order.Error!;
            }

            orderIds.Add(order.Value);
        }

        var requisitionIds = rfq.Items.Where(i => i.RequisitionId is not null && decisions.Any(d => d.ItemLineNumber == i.LineNumber))
            .Select(i => i.RequisitionId!.Value).Distinct().ToList();
        await requisitions.MarkOrderedAsync(requisitionIds, cancellationToken);

        await db.SaveChangesAsync(cancellationToken);
        return orderIds;
    }
}
