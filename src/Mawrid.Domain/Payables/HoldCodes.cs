namespace Mawrid.Domain.Payables;

/// <summary>Why an invoice is on hold. The codes are stable: clients and reports use them.</summary>
public static class HoldCodes
{
    public const string QrMissing = "qr.missing";
    public const string QrMalformed = "qr.malformed";
    public const string QrVatMismatch = "qr.vat_mismatch";
    public const string QrTotalMismatch = "qr.total_mismatch";
    public const string LineAmount = "line.amount";
    public const string VatCalculation = "vat.calculation";
    public const string TotalsMismatch = "totals.mismatch";
    public const string MatchLineUnknown = "match.line_unknown";
    public const string MatchPrice = "match.price";
    public const string MatchQuantity = "match.quantity";
    public const string SupplierBankUnverified = "supplier.bank_unverified";

    /// <summary>These clear only when the cause is fixed and the invoice is checked again.</summary>
    public static readonly IReadOnlySet<string> NotOverridable = new HashSet<string> { MatchLineUnknown, MatchQuantity, SupplierBankUnverified };

    /// <summary>Overriding these needs an admin (a finance manager), not an AP clerk.</summary>
    public static readonly IReadOnlySet<string> AdminOverride = new HashSet<string> { MatchPrice };
}
