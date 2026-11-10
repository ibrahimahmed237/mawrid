using Mawrid.Domain.Common;

namespace Mawrid.Domain.Requisitions;

public static class RequisitionErrors
{
    public static readonly Error TitleInvalid =
        Error.Validation("requisition.title_invalid", "The title must be 3 to 200 characters.");

    public static readonly Error JustificationInvalid =
        Error.Validation("requisition.justification_invalid", "The justification must be 10 to 2000 characters.");

    public static readonly Error NeededByInPast =
        Error.Validation("requisition.needed_by_in_past", "The needed-by date must be after today.");

    public static readonly Error LinesRequired =
        Error.Validation("requisition.lines_required", $"A requisition needs 1 to {PurchaseRequisition.MaxLines} lines.");

    public static readonly Error NotDraft =
        Error.RuleViolation("requisition.not_draft", "Only a draft requisition can be changed or submitted.");

    public static readonly Error NotFound =
        Error.NotFound("requisition.not_found", "The requisition was not found.");

    public static readonly Error NotRequesterOfCostCenter =
        Error.Forbidden("requisition.not_requester", "You are not a requester for this cost center.");

    public static readonly Error NotOwner =
        Error.Forbidden("requisition.not_owner", "Only the requester can change this requisition.");

    public static Error LineInvalid(int lineNumber) =>
        Error.Validation("requisition.line_invalid", $"Line {lineNumber}: description, unit, a positive quantity (max 3 decimals) and a price of 0 or more (max 4 decimals) are required.");

    public static Error AboveDirectPurchaseLimit(decimal limit) =>
        Error.RuleViolation("requisition.above_direct_limit", $"A direct purchase is allowed only up to {limit:N2} SAR. Use an RFQ.");

    public static Error InvalidStatusChange(RequisitionStatus from, RequisitionStatus to) =>
        Error.RuleViolation("requisition.invalid_status_change", $"A requisition cannot move from {from} to {to}.");
}
