namespace Mawrid.Application.Sourcing;

public sealed record RfqSummaryDto(Guid Id, string Number, string Title, string Status, DateTimeOffset? Deadline, int InvitedCount);

public sealed record RfqDto(
    Guid Id,
    string Number,
    string Title,
    string? Description,
    string Status,
    Guid CostCenterId,
    DateTimeOffset? Deadline,
    int Revision,
    string? SingleSourceReason,
    IReadOnlyList<RfqItemDto> Items,
    IReadOnlyList<InvitedSupplierDto> InvitedSuppliers,
    IReadOnlyList<RfqAwardDto> Awards);

public sealed record RfqItemDto(int LineNumber, string Description, decimal Quantity, string Unit, Guid? RequisitionId);

public sealed record InvitedSupplierDto(Guid SupplierId, string NameEn, string NameAr);

public sealed record RfqAwardDto(int ItemLineNumber, Guid SupplierId, decimal UnitPrice, bool IsLowestPrice, string? Justification);

/// <summary>Who submitted and when. Never prices: those stay sealed until the deadline.</summary>
public sealed record SubmissionDto(Guid SupplierId, string SupplierName, DateTimeOffset SubmittedAtUtc, int Version, bool NeedsReconfirmation);

public sealed record ComparisonDto(Guid RfqId, string Number, IReadOnlyList<ComparisonItemDto> Items, IReadOnlyList<SupplierTotalDto> Suppliers);

public sealed record ComparisonItemDto(int LineNumber, string Description, decimal Quantity, string Unit, IReadOnlyList<ComparisonQuoteDto> Quotes);

/// <param name="IsLowest">The lowest unit price among the eligible quotes for this item.</param>
/// <param name="IsEligible">Valid today and answering the latest RFQ revision; only eligible quotes can be awarded.</param>
public sealed record ComparisonQuoteDto(Guid SupplierId, string SupplierName, Guid QuotationId, decimal UnitPrice, string VatCategory, int LeadTimeDays,
    decimal LineNet, bool IsLowest, bool IsEligible);

public sealed record SupplierTotalDto(Guid SupplierId, string SupplierName, decimal NetTotalOfQuotedItems, int ItemsQuoted, DateOnly ValidUntil, bool NeedsReconfirmation);
