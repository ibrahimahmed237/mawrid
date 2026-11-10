using Mawrid.Domain.Common;

namespace Mawrid.Domain.Purchasing;

public static class PurchasingErrors
{
    public static readonly Error LinesInvalid = Error.Validation("purchase_order.lines_invalid", "A PO needs lines with a description, a positive quantity and a price of 0 or more.");

    public static readonly Error NotFound = Error.NotFound("purchase_order.not_found", "The purchase order was not found.");

    public static readonly Error NotReceivable = Error.RuleViolation("purchase_order.not_receivable", "Goods can be received only for a sent or acknowledged PO.");

    public static readonly Error ReceiptInvalid = Error.Validation("receipt.invalid", "Each receipt line must refer to a PO line once, with quantities of 0 or more (not both 0).");

    public static readonly Error RejectionReasonRequired = Error.Validation("receipt.rejection_reason", "A rejected quantity needs a reason, and a reason needs a rejected quantity.");

    public static readonly Error CannotCancel = Error.RuleViolation("purchase_order.cannot_cancel", "A PO cannot be cancelled after goods were received.");

    public static readonly Error CannotClose = Error.RuleViolation("purchase_order.cannot_close", "A PO can be closed only when fully received and fully invoiced.");

    public static readonly Error ReasonRequired = Error.Validation("purchase_order.reason_required", "A reason of 3 to 500 characters is required.");

    public static readonly Error SupplierNotAwardable = Error.RuleViolation("purchase_order.supplier_not_awardable", "The supplier is not approved or its documents expired.");

    public static Error OverDelivery(int line) => Error.RuleViolation("receipt.over_delivery", $"Line {line}: more was received than ordered (plus tolerance).");

    public static Error InvalidStatusChange(PurchaseOrderStatus from, PurchaseOrderStatus to) =>
        Error.RuleViolation("purchase_order.invalid_status_change", $"A purchase order cannot move from {from} to {to}.");
}
