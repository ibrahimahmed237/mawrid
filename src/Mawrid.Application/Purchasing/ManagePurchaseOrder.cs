using FluentValidation;
using Mawrid.Application.Common.Abstractions;
using Mawrid.Application.Common.Messaging;
using Mawrid.Application.Common.Persistence;
using Mawrid.Application.Common.Security;
using Mawrid.Application.Contracts.Approvals;
using Mawrid.Application.Contracts.Identity;
using Mawrid.Application.Contracts.Purchasing;
using Mawrid.Application.Contracts.Requisitions;
using Mawrid.Application.Contracts.Suppliers;
using Mawrid.Domain.Common;
using Mawrid.Domain.Purchasing;
using Microsoft.EntityFrameworkCore;

namespace Mawrid.Application.Purchasing;

// Every state change takes an optional ExpectedVersion (from If-Match). A different version gives 412.

public sealed record DirectPurchaseLineDto(int RequisitionLineNumber, decimal UnitPrice, string VatCategory);

/// <summary>Buys a small requisition straight from one supplier, without an RFQ.</summary>
public sealed record CreateDirectPurchaseOrderCommand(Guid RequisitionId, Guid SupplierId, IReadOnlyList<DirectPurchaseLineDto> Lines)
    : ICommand<Guid>;

internal sealed class CreateDirectPurchaseOrderCommandValidator : AbstractValidator<CreateDirectPurchaseOrderCommand>
{
    public CreateDirectPurchaseOrderCommandValidator()
    {
        RuleFor(x => x.Lines).NotEmpty();
        RuleForEach(x => x.Lines).ChildRules(line =>
        {
            line.RuleFor(l => l.UnitPrice).GreaterThanOrEqualTo(0).PrecisionScale(19, 4, ignoreTrailingZeros: true);
            line.RuleFor(l => l.VatCategory).Must(v => Enum.TryParse<VatCategory>(v, ignoreCase: true, out _))
                .WithMessage($"VAT category must be one of: {string.Join(", ", Enum.GetNames<VatCategory>())}.");
        });
    }
}

internal sealed class CreateDirectPurchaseOrderCommandHandler(
    IAppDbContext db,
    IRequisitionSourcing requisitions,
    ISupplierDirectory suppliers,
    IOrganizationDirectory organization,
    IPurchaseOrderDrafts drafts) : ICommandHandler<CreateDirectPurchaseOrderCommand, Guid>
{
    public async Task<Result<Guid>> Handle(CreateDirectPurchaseOrderCommand command, CancellationToken cancellationToken)
    {
        var source = await requisitions.GetForDirectPurchaseAsync(command.RequisitionId, cancellationToken);
        if (source.IsFailure)
        {
            return source.Error!;
        }

        var supplier = await suppliers.FindAsync(command.SupplierId, cancellationToken);
        if (supplier is not { IsAwardable: true })
        {
            return PurchasingErrors.SupplierNotAwardable;
        }

        var requisitionLines = source.Value.Lines;
        var prices = command.Lines.DistinctBy(l => l.RequisitionLineNumber).ToDictionary(l => l.RequisitionLineNumber);
        if (prices.Count != command.Lines.Count || prices.Count != requisitionLines.Count || requisitionLines.Any(l => !prices.ContainsKey(l.LineNumber)))
        {
            return PurchasingErrors.DirectPurchaseLinesInvalid;
        }

        var lines = requisitionLines.Select(l => new PurchaseOrderDraftLine(
            l.Description, l.Quantity, l.Unit, prices[l.LineNumber].UnitPrice,
            Enum.Parse<VatCategory>(prices[l.LineNumber].VatCategory, ignoreCase: true))).ToList();

        // The route was chosen on estimates; the real prices must also fit the limit.
        var settings = await organization.GetSettingsAsync(cancellationToken);
        if (lines.Sum(l => Money.Net(l.Quantity, l.UnitPrice)) > settings.DirectPurchaseLimit)
        {
            return PurchasingErrors.OverDirectPurchaseLimit;
        }

        var order = await drafts.CreateDraftAsync(new PurchaseOrderDraft(
            command.SupplierId, source.Value.CostCenterId, PurchaseOrderSources.DirectPurchase, command.RequisitionId, [command.RequisitionId], lines),
            cancellationToken);
        if (order.IsFailure)
        {
            return order.Error!;
        }

        await requisitions.MarkOrderedAsync([command.RequisitionId], cancellationToken);
        await db.SaveChangesAsync(cancellationToken);
        return order.Value;
    }
}

public sealed record UpdatePurchaseOrderTermsCommand(Guid PurchaseOrderId, int PaymentTermsDays, string? DeliveryAddress, string? ExpectedVersion)
    : ICommand;

internal sealed class UpdatePurchaseOrderTermsCommandHandler(IAppDbContext db) : ICommandHandler<UpdatePurchaseOrderTermsCommand>
{
    public async Task<Result> Handle(UpdatePurchaseOrderTermsCommand command, CancellationToken cancellationToken)
    {
        var order = await PurchaseOrderChanges.LoadAsync(db, command.PurchaseOrderId, command.ExpectedVersion, cancellationToken);
        return order.IsFailure
            ? order
            : await PurchaseOrderChanges.SaveIfSuccessAsync(db, order.Value.UpdateTerms(command.PaymentTermsDays, command.DeliveryAddress), cancellationToken);
    }
}

/// <summary>Sends a draft PO for approval with the PO approval policy. The supplier must still be awardable.</summary>
public sealed record SubmitPurchaseOrderCommand(Guid PurchaseOrderId, string? ExpectedVersion) : ICommand;

internal sealed class SubmitPurchaseOrderCommandHandler(
    IAppDbContext db, ICurrentUser currentUser, ISupplierDirectory suppliers, IApprovalService approvals)
    : ICommandHandler<SubmitPurchaseOrderCommand>
{
    public async Task<Result> Handle(SubmitPurchaseOrderCommand command, CancellationToken cancellationToken)
    {
        var loaded = await PurchaseOrderChanges.LoadAsync(db, command.PurchaseOrderId, command.ExpectedVersion, cancellationToken);
        if (loaded.IsFailure)
        {
            return loaded;
        }

        var order = loaded.Value;
        if (order.Status != PurchaseOrderStatus.Draft)
        {
            return PurchasingErrors.NotDraft;
        }

        var supplier = await suppliers.FindAsync(order.SupplierId, cancellationToken);
        if (supplier is not { IsAwardable: true })
        {
            return PurchasingErrors.SupplierNotAwardable;
        }

        var approval = await approvals.StartAsync(new ApprovalStart(
            ApprovalDocumentTypes.PurchaseOrder,
            order.Id,
            $"{order.Number} · {supplier.NameEn}",
            order.Total,
            order.CostCenterId,
            currentUser.UserId!.Value), cancellationToken);
        if (approval.IsFailure)
        {
            return approval.Error!;
        }

        return await PurchaseOrderChanges.SaveIfSuccessAsync(db, order.Submit(approval.Value), cancellationToken);
    }
}

/// <summary>Sends an approved PO to the supplier. From now on the supplier can see it.</summary>
public sealed record SendPurchaseOrderCommand(Guid PurchaseOrderId, string? ExpectedVersion) : ICommand;

internal sealed class SendPurchaseOrderCommandHandler(IAppDbContext db, IClock clock) : ICommandHandler<SendPurchaseOrderCommand>
{
    public async Task<Result> Handle(SendPurchaseOrderCommand command, CancellationToken cancellationToken)
    {
        var order = await PurchaseOrderChanges.LoadAsync(db, command.PurchaseOrderId, command.ExpectedVersion, cancellationToken);
        return order.IsFailure
            ? order
            : await PurchaseOrderChanges.SaveIfSuccessAsync(db, order.Value.Send(clock.UtcNow), cancellationToken);
    }
}

/// <summary>Cancels a PO before anything was received. A pending approval is cancelled too.</summary>
public sealed record CancelPurchaseOrderCommand(Guid PurchaseOrderId, string Reason, string? ExpectedVersion) : ICommand;

internal sealed class CancelPurchaseOrderCommandHandler(IAppDbContext db, IApprovalService approvals) : ICommandHandler<CancelPurchaseOrderCommand>
{
    public async Task<Result> Handle(CancelPurchaseOrderCommand command, CancellationToken cancellationToken)
    {
        var loaded = await PurchaseOrderChanges.LoadAsync(db, command.PurchaseOrderId, command.ExpectedVersion, cancellationToken);
        if (loaded.IsFailure)
        {
            return loaded;
        }

        var order = loaded.Value;
        var pendingApproval = order.Status == PurchaseOrderStatus.PendingApproval ? order.ApprovalRequestId : null;
        var result = order.Cancel(command.Reason);
        if (result.IsFailure)
        {
            return result;
        }

        if (pendingApproval is { } approvalId)
        {
            await approvals.CancelAsync(approvalId, cancellationToken);
        }

        await db.SaveChangesAsync(cancellationToken);
        return Result.Success();
    }
}

/// <summary>Closes a finished PO, or short closes a partly received one with a reason.</summary>
public sealed record ClosePurchaseOrderCommand(Guid PurchaseOrderId, string? Reason, string? ExpectedVersion) : ICommand;

internal sealed class ClosePurchaseOrderCommandHandler(IAppDbContext db) : ICommandHandler<ClosePurchaseOrderCommand>
{
    public async Task<Result> Handle(ClosePurchaseOrderCommand command, CancellationToken cancellationToken)
    {
        var order = await PurchaseOrderChanges.LoadAsync(db, command.PurchaseOrderId, command.ExpectedVersion, cancellationToken);
        return order.IsFailure
            ? order
            : await PurchaseOrderChanges.SaveIfSuccessAsync(db, order.Value.Close(command.Reason), cancellationToken);
    }
}

public sealed record ReceiptLineDto(int LineNumber, decimal AcceptedQuantity, decimal RejectedQuantity = 0, string? RejectionReason = null);

/// <summary>Records a delivery against a sent PO. The over-delivery tolerance comes from the organization settings.</summary>
public sealed record RecordGoodsReceiptCommand(Guid PurchaseOrderId, IReadOnlyList<ReceiptLineDto> Lines, string? Note) : ICommand<Guid>;

internal sealed class RecordGoodsReceiptCommandValidator : AbstractValidator<RecordGoodsReceiptCommand>
{
    public RecordGoodsReceiptCommandValidator()
    {
        RuleFor(x => x.Lines).NotEmpty();
        RuleFor(x => x.Note).MaximumLength(PurchaseOrder.ReasonMaxLength);
        RuleForEach(x => x.Lines).ChildRules(line =>
        {
            line.RuleFor(l => l.AcceptedQuantity).GreaterThanOrEqualTo(0).PrecisionScale(18, 3, ignoreTrailingZeros: true);
            line.RuleFor(l => l.RejectedQuantity).GreaterThanOrEqualTo(0).PrecisionScale(18, 3, ignoreTrailingZeros: true);
            line.RuleFor(l => l.RejectionReason).MaximumLength(PurchaseOrder.ReasonMaxLength);
        });
    }
}

internal sealed class RecordGoodsReceiptCommandHandler(IAppDbContext db, ICurrentUser currentUser, IOrganizationDirectory organization, IClock clock)
    : ICommandHandler<RecordGoodsReceiptCommand, Guid>
{
    public async Task<Result<Guid>> Handle(RecordGoodsReceiptCommand command, CancellationToken cancellationToken)
    {
        var order = await db.PurchaseOrders.SingleOrDefaultAsync(o => o.Id == command.PurchaseOrderId, cancellationToken);
        if (order is null)
        {
            return PurchasingErrors.NotFound;
        }

        var settings = await organization.GetSettingsAsync(cancellationToken);
        var lines = command.Lines.Select(l => new ReceiptLineInput(l.LineNumber, l.AcceptedQuantity, l.RejectedQuantity, l.RejectionReason)).ToList();
        var receipt = order.RecordReceipt(currentUser.UserId!.Value, lines, settings.OverDeliveryPercent, command.Note, clock.UtcNow);
        if (receipt.IsSuccess)
        {
            await db.SaveChangesAsync(cancellationToken);
        }

        return receipt;
    }
}

/// <summary>The supplier confirms it received a sent PO.</summary>
public sealed record AcknowledgePurchaseOrderCommand(Guid PurchaseOrderId) : ICommand;

internal sealed class AcknowledgePurchaseOrderCommandHandler(IAppDbContext db, ICurrentUser currentUser, IClock clock)
    : ICommandHandler<AcknowledgePurchaseOrderCommand>
{
    public async Task<Result> Handle(AcknowledgePurchaseOrderCommand command, CancellationToken cancellationToken)
    {
        // A supplier sees only its own POs, and only after they were sent; anything else is "not found".
        var order = await db.PurchaseOrders.SingleOrDefaultAsync(
            o => o.Id == command.PurchaseOrderId && o.SupplierId == currentUser.SupplierId && o.SentAtUtc != null, cancellationToken);
        if (order is null)
        {
            return PurchasingErrors.NotFound;
        }

        return await PurchaseOrderChanges.SaveIfSuccessAsync(db, order.Acknowledge(clock.UtcNow), cancellationToken);
    }
}

/// <summary>Called by the Approvals module when a PO's approval finishes, in the same transaction.</summary>
internal sealed class PurchaseOrderApprovalHandler(IAppDbContext db) : IApprovalDocumentHandler
{
    public string DocumentType => ApprovalDocumentTypes.PurchaseOrder;

    public async Task<Result> OnApprovedAsync(Guid documentId, CancellationToken cancellationToken)
    {
        var order = await db.PurchaseOrders.SingleAsync(o => o.Id == documentId, cancellationToken);
        return order.MarkApproved();
    }

    public async Task<Result> OnRejectedAsync(Guid documentId, string reason, CancellationToken cancellationToken)
    {
        var order = await db.PurchaseOrders.SingleAsync(o => o.Id == documentId, cancellationToken);
        return order.MarkRejected(reason);
    }
}

internal static class PurchaseOrderChanges
{
    /// <summary>Loads a PO for a change, checking the version the client saw.</summary>
    public static async Task<Result<PurchaseOrder>> LoadAsync(IAppDbContext db, Guid id, string? expectedVersion, CancellationToken cancellationToken)
    {
        var order = await db.PurchaseOrders.SingleOrDefaultAsync(o => o.Id == id, cancellationToken);
        if (order is null)
        {
            return PurchasingErrors.NotFound;
        }

        return RowVersions.IsStale(expectedVersion, order.RowVersion) ? PurchasingErrors.StaleVersion : order;
    }

    /// <summary>Saves the unit of work when the domain change succeeded.</summary>
    public static async Task<Result> SaveIfSuccessAsync(IAppDbContext db, Result result, CancellationToken cancellationToken)
    {
        if (result.IsSuccess)
        {
            await db.SaveChangesAsync(cancellationToken);
        }

        return result;
    }
}
