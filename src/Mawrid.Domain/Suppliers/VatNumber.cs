using Mawrid.Domain.Common;

namespace Mawrid.Domain.Suppliers;

/// <summary>A Saudi VAT registration number: 15 digits that start and end with 3.</summary>
public sealed record VatNumber
{
    private VatNumber(string value) => Value = value;

    public string Value { get; }

    public static Result<VatNumber> Create(string? value)
    {
        var digits = value?.Trim() ?? string.Empty;
        var valid = digits.Length == 15 && digits.All(char.IsAsciiDigit) && digits[0] == '3' && digits[^1] == '3';
        return valid ? new VatNumber(digits) : SupplierErrors.VatNumberInvalid;
    }

    public override string ToString() => Value;
}

/// <summary>A Saudi commercial registration (CR) number: 10 digits.</summary>
public sealed record CrNumber
{
    private CrNumber(string value) => Value = value;

    public string Value { get; }

    public static Result<CrNumber> Create(string? value)
    {
        var digits = value?.Trim() ?? string.Empty;
        return digits.Length == 10 && digits.All(char.IsAsciiDigit) ? new CrNumber(digits) : SupplierErrors.CrNumberInvalid;
    }

    public override string ToString() => Value;
}
