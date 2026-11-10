using System.Numerics;
using Mawrid.Domain.Common;

namespace Mawrid.Domain.Suppliers;

/// <summary>
/// A Saudi IBAN: "SA" + 2 check digits + 20 characters (24 in total), with a valid
/// ISO 13616 mod-97 checksum. Spaces are removed and letters are upper-cased.
/// </summary>
public sealed record Iban
{
    private Iban(string value) => Value = value;

    public string Value { get; }

    /// <summary>For display: only the last 4 characters, such as <c>SA** **** **** **** **** 1234</c>.</summary>
    public string Masked => $"SA** **** **** **** **** {Value[^4..]}";

    public static Result<Iban> Create(string? value)
    {
        var iban = new string((value ?? string.Empty).Where(c => !char.IsWhiteSpace(c)).ToArray()).ToUpperInvariant();

        if (iban.Length != 24 || !iban.StartsWith("SA", StringComparison.Ordinal) || !iban.All(char.IsAsciiLetterOrDigit))
        {
            return SupplierErrors.IbanInvalid;
        }

        return HasValidChecksum(iban) ? new Iban(iban) : SupplierErrors.IbanInvalid;
    }

    /// <summary>Move the first 4 characters to the end, turn letters into numbers (A=10 ... Z=35), and check the number mod 97 is 1.</summary>
    private static bool HasValidChecksum(string iban)
    {
        var rearranged = iban[4..] + iban[..4];
        var digits = string.Concat(rearranged.Select(c => char.IsAsciiDigit(c) ? c.ToString() : (c - 'A' + 10).ToString()));
        return BigInteger.Parse(digits) % 97 == 1;
    }

    public override string ToString() => Value;
}
