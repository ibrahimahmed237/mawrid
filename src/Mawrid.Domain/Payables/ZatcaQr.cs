using System.Globalization;
using System.Text;
using Mawrid.Domain.Common;

namespace Mawrid.Domain.Payables;

/// <summary>
/// The QR code on a ZATCA e-invoice: Base64 of TLV records (1 byte tag, 1 byte length, UTF-8 value).
/// Tags 1–5 are seller name, VAT number, timestamp, total with VAT, and VAT total. Phase 2 adds
/// tags 6–9 (hash, signature, public key, stamp); we read past them but do not verify them.
/// </summary>
public sealed record ZatcaQr(string SellerName, string VatNumber, string Timestamp, decimal Total, decimal VatTotal)
{
    public const int MaxLength = 4000;

    private const byte SellerNameTag = 1;
    private const byte VatNumberTag = 2;
    private const byte TimestampTag = 3;
    private const byte TotalTag = 4;
    private const byte VatTotalTag = 5;

    public static Result<ZatcaQr> Decode(string? base64)
    {
        if (string.IsNullOrWhiteSpace(base64) || base64.Length > MaxLength)
        {
            return PayablesErrors.QrMalformed;
        }

        byte[] bytes;
        try
        {
            bytes = Convert.FromBase64String(base64.Trim());
        }
        catch (FormatException)
        {
            return PayablesErrors.QrMalformed;
        }

        var values = new Dictionary<byte, string>();
        var position = 0;
        while (position < bytes.Length)
        {
            if (position + 2 > bytes.Length)
            {
                return PayablesErrors.QrMalformed;
            }

            var tag = bytes[position];
            var length = bytes[position + 1];
            position += 2;
            if (position + length > bytes.Length)
            {
                return PayablesErrors.QrMalformed;
            }

            values[tag] = Encoding.UTF8.GetString(bytes, position, length);
            position += length;
        }

        if (!values.TryGetValue(SellerNameTag, out var seller)
            || !values.TryGetValue(VatNumberTag, out var vatNumber)
            || !values.TryGetValue(TimestampTag, out var timestamp)
            || !TryAmount(values, TotalTag, out var total)
            || !TryAmount(values, VatTotalTag, out var vatTotal))
        {
            return PayablesErrors.QrMalformed;
        }

        return new ZatcaQr(seller, vatNumber, timestamp, total, vatTotal);
    }

    /// <summary>Builds the Base64 TLV text, as a supplier's e-invoicing system would.</summary>
    public string Encode()
    {
        using var stream = new MemoryStream();
        Write(stream, SellerNameTag, SellerName);
        Write(stream, VatNumberTag, VatNumber);
        Write(stream, TimestampTag, Timestamp);
        Write(stream, TotalTag, Total.ToString("0.00", CultureInfo.InvariantCulture));
        Write(stream, VatTotalTag, VatTotal.ToString("0.00", CultureInfo.InvariantCulture));
        return Convert.ToBase64String(stream.ToArray());
    }

    private static void Write(Stream stream, byte tag, string value)
    {
        var bytes = Encoding.UTF8.GetBytes(value);
        if (bytes.Length > byte.MaxValue)
        {
            throw new ArgumentException($"QR tag {tag} is longer than 255 bytes.", nameof(value));
        }

        stream.WriteByte(tag);
        stream.WriteByte((byte)bytes.Length);
        stream.Write(bytes);
    }

    private static bool TryAmount(Dictionary<byte, string> values, byte tag, out decimal amount)
    {
        amount = 0;
        return values.TryGetValue(tag, out var text)
            && decimal.TryParse(text, NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out amount);
    }
}
