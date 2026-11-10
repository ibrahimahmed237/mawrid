namespace Mawrid.Domain.Common;

/// <summary>KSA VAT categories (ZATCA): standard rate, zero-rated, or exempt.</summary>
public enum VatCategory
{
    Standard = 1,
    ZeroRated = 2,
    Exempt = 3,
}

/// <summary>
/// VAT rates with the date they started. KSA VAT was 5% from 1 Jan 2018 and 15% from
/// 1 Jul 2020. A fixed constant would calculate old documents wrongly (DESIGN 6.7).
/// </summary>
public static class VatRates
{
    private static readonly (DateOnly ValidFrom, decimal Rate)[] StandardRates =
    [
        (new DateOnly(2018, 1, 1), 0.05m),
        (new DateOnly(2020, 7, 1), 0.15m),
    ];

    public static decimal RateFor(VatCategory category, DateOnly date) => category switch
    {
        VatCategory.Standard => StandardRates.Last(r => r.ValidFrom <= date).Rate,
        _ => 0m,
    };
}

/// <summary>Money rules: SAR amounts, rounded half away from zero to 2 decimals.</summary>
public static class Money
{
    public const string Currency = "SAR";

    public static decimal Round(decimal amount) => Math.Round(amount, 2, MidpointRounding.AwayFromZero);

    /// <summary>Net amount of a line: quantity × unit price, rounded.</summary>
    public static decimal Net(decimal quantity, decimal unitPrice) => Round(quantity * unitPrice);

    /// <summary>VAT of a line: net × rate, rounded per line.</summary>
    public static decimal Vat(decimal net, decimal rate) => Round(net * rate);
}
