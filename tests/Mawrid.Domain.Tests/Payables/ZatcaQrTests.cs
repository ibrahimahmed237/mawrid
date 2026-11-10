using System.Text;
using Mawrid.Domain.Payables;

namespace Mawrid.Domain.Tests.Payables;

public class ZatcaQrTests
{
    private static readonly ZatcaQr Sample = new("شركة الأفق للتوريدات", "310122393500003", "2026-11-10T09:30:00Z", 1150.00m, 150.00m);

    [Fact]
    public void Encode_then_decode_returns_the_same_values()
    {
        var decoded = ZatcaQr.Decode(Sample.Encode());

        decoded.IsSuccess.Should().BeTrue();
        decoded.Value.Should().Be(Sample);
    }

    [Fact]
    public void Decodes_the_tlv_bytes_by_hand()
    {
        // Tag 1 "Bobs", tag 2 VAT, tag 3 time, tag 4 total, tag 5 VAT total.
        var bytes = new List<byte>();
        void Add(byte tag, string value)
        {
            var v = Encoding.UTF8.GetBytes(value);
            bytes.Add(tag);
            bytes.Add((byte)v.Length);
            bytes.AddRange(v);
        }

        Add(1, "Bobs Records");
        Add(2, "310122393500003");
        Add(3, "2022-04-25T15:30:00Z");
        Add(4, "1000.00");
        Add(5, "150.00");

        var qr = ZatcaQr.Decode(Convert.ToBase64String(bytes.ToArray())).Value;

        qr.SellerName.Should().Be("Bobs Records");
        qr.Total.Should().Be(1000.00m);
        qr.VatTotal.Should().Be(150.00m);
    }

    [Fact]
    public void Extra_phase_2_tags_are_skipped()
    {
        var bytes = Convert.FromBase64String(Sample.Encode()).Concat(new byte[] { 6, 3, 0x61, 0x62, 0x63 }).ToArray();

        ZatcaQr.Decode(Convert.ToBase64String(bytes)).IsSuccess.Should().BeTrue();
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("not base64!")]
    [InlineData("AQ==")] // a tag without a length
    [InlineData("AQVBQg==")] // length 5 but only 2 bytes follow
    public void Bad_input_is_malformed(string? input)
    {
        ZatcaQr.Decode(input).Error.Should().Be(PayablesErrors.QrMalformed);
    }

    [Fact]
    public void A_missing_tag_is_malformed()
    {
        var withoutVatTotal = Convert.FromBase64String(Sample.Encode());
        var cut = withoutVatTotal.Take(withoutVatTotal.Length - 8).ToArray(); // drop tag 5 (2 + "150.00")

        ZatcaQr.Decode(Convert.ToBase64String(cut)).Error.Should().Be(PayablesErrors.QrMalformed);
    }
}
