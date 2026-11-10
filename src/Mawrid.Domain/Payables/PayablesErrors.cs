using Mawrid.Domain.Common;

namespace Mawrid.Domain.Payables;

public static class PayablesErrors
{
    public static readonly Error NotFound = Error.NotFound("invoice.not_found", "The invoice was not found.");

    public static readonly Error QrMalformed = Error.Validation("qr.malformed", "The QR code is not valid ZATCA TLV data.");

    public static readonly Error DetailsInvalid = Error.Validation(
        "invoice.details_invalid", "An invoice needs a number (1–50 characters), an issue date from 2018 up to today, and at least one line.");

    public static readonly Error LinesInvalid = Error.Validation(
        "invoice.lines_invalid", "Each line must refer to a different PO line, with a positive quantity and amounts of 0 or more.");

    public static readonly Error Duplicate = Error.Conflict("invoice.duplicate", "This supplier already sent an invoice with this number.");

    public static readonly Error PurchaseOrderNotInvoiceable = Error.RuleViolation(
        "invoice.po_not_invoiceable", "Invoices can be sent only for a PO that was sent to this supplier and not cancelled.");

    public static readonly Error HoldNotFound = Error.NotFound("invoice.hold_not_found", "The invoice has no open hold with this code and line.");

    public static readonly Error HoldNotOverridable = Error.RuleViolation(
        "invoice.hold_not_overridable", "This hold cannot be overridden. Fix the cause (receive the goods, verify the bank account) and check again.");

    public static readonly Error OverrideNeedsAdmin = Error.Forbidden("invoice.override_needs_admin", "Only an admin can override a price difference.");

    public static readonly Error ReasonRequired = Error.Validation("invoice.reason_required", "A reason of 10 to 500 characters is required.");

    public static readonly Error SamePersonCannotRelease = Error.Forbidden(
        "invoice.same_person", "The person who typed in an invoice cannot also release it for payment.");

    public static readonly Error SourceFileInvalid = Error.Validation("invoice.source_file_invalid", "The invoice file could not be read as a UBL 2.1 invoice.");

    public static Error InvalidStatusChange(InvoiceStatus from, InvoiceStatus to) =>
        Error.RuleViolation("invoice.invalid_status_change", $"An invoice cannot move from {from} to {to}.");
}
