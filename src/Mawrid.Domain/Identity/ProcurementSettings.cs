using Mawrid.Domain.Common;

namespace Mawrid.Domain.Identity;

/// <summary>Per-organization procurement settings. The defaults follow DESIGN sections 6.4 to 6.7.</summary>
/// <param name="DirectPurchaseLimit">Largest estimated net total (SAR) that can be bought without an RFQ.</param>
/// <param name="MinimumSuppliersPerRfq">Fewer invitations than this need a single-source reason.</param>
/// <param name="MinimumRfqWindowDays">Days between publishing an RFQ and its deadline, at least.</param>
/// <param name="PriceTolerancePercent">How much an invoice unit price may exceed the PO price, in percent.</param>
/// <param name="PriceToleranceMaxAmount">The same tolerance, capped per line in SAR.</param>
/// <param name="OverDeliveryPercent">How much more than ordered may be received, in percent.</param>
public sealed record ProcurementSettings(
    decimal DirectPurchaseLimit,
    int MinimumSuppliersPerRfq,
    int MinimumRfqWindowDays,
    decimal PriceTolerancePercent,
    decimal PriceToleranceMaxAmount,
    decimal OverDeliveryPercent)
{
    public static ProcurementSettings Default { get; } = new(5_000m, 3, 3, 2m, 500m, 0m);

    public Result Validate()
    {
        var valid = DirectPurchaseLimit >= 0
            && MinimumSuppliersPerRfq is >= 1 and <= 20
            && MinimumRfqWindowDays is >= 1 and <= 60
            && PriceTolerancePercent is >= 0 and <= 20
            && PriceToleranceMaxAmount >= 0
            && OverDeliveryPercent is >= 0 and <= 20;

        return valid ? Result.Success() : IdentityErrors.SettingsInvalid;
    }
}
