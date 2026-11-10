using Mawrid.Domain.Common;

namespace Mawrid.Domain.Sourcing;

public static class SourcingErrors
{
    public static readonly Error TitleInvalid = Error.Validation("rfq.title_invalid", "The title must be 3 to 200 characters; the description at most 4000.");

    public static readonly Error ItemsInvalid = Error.Validation("rfq.items_invalid", $"An RFQ needs 1 to {Rfq.MaxItems} items, each with a description and a positive quantity.");

    public static readonly Error NotDraft = Error.RuleViolation("rfq.not_draft", "Only a draft RFQ can be changed this way.");

    public static readonly Error NoInvitations = Error.RuleViolation("rfq.no_invitations", "Invite at least one supplier before publishing.");

    public static readonly Error SupplierNotAwardable = Error.RuleViolation("rfq.supplier_not_awardable", "Only approved suppliers with valid documents can be invited.");

    public static readonly Error NotOpen = Error.RuleViolation("rfq.deadline_passed", "The RFQ is not open for quotations: the deadline has passed or it is not published.");

    public static readonly Error NotInvited = Error.NotFound("rfq.not_found", "The RFQ was not found.");

    public static readonly Error DeadlineNotPassed = Error.RuleViolation("rfq.still_open", "Quotations stay sealed until the deadline passes.");

    public static readonly Error AlreadyAwarded = Error.RuleViolation("rfq.already_awarded", "This RFQ was already awarded.");

    public static readonly Error AwardInvalid = Error.Validation("rfq.award_invalid", "Each awarded item must exist, appear once, and go to an invited supplier's quotation line.");

    public static readonly Error ReasonRequired = Error.Validation("rfq.reason_required", "A reason of 3 to 500 characters is required.");

    public static readonly Error ValidityInvalid = Error.Validation("quotation.validity_invalid", "The quotation must be valid until a future date.");

    public static readonly Error QuotationLinesInvalid = Error.Validation("quotation.lines_invalid", "Each line must refer to an RFQ item once, with a price above 0 (max 4 decimals) and a lead time of 0 to 365 days.");

    public static readonly Error NotFound = Error.NotFound("rfq.not_found", "The RFQ was not found.");

    public static readonly Error RequisitionsInvalid = Error.RuleViolation("rfq.requisitions_invalid", "Requisitions must be approved for RFQ sourcing and share one cost center.");

    public static readonly Error AttachmentNotFound = Error.NotFound("quotation.attachment_not_found", "An attachment was not found, or it belongs to someone else.");

    public static readonly Error QuotationNotFound = Error.NotFound("quotation.not_found", "No quotation was found.");

    public static Error DeadlineTooSoon(int days) => Error.RuleViolation("rfq.deadline_too_soon", $"The deadline must be at least {days} days from now, and not earlier than before.");

    public static Error SingleSourceReasonRequired(int minimum) =>
        Error.Validation("rfq.single_source_reason_required", $"Fewer than {minimum} suppliers are invited. Give a single-source reason of 10 to 500 characters.");

    public static Error JustificationRequired(int line) =>
        Error.Validation("rfq.justification_required", $"Item {line} is not awarded to the lowest price. Give a justification of at least 10 characters.");

    public static Error InvalidStatusChange(RfqStatus from, RfqStatus to) =>
        Error.RuleViolation("rfq.invalid_status_change", $"An RFQ cannot move from {from} to {to}.");
}
