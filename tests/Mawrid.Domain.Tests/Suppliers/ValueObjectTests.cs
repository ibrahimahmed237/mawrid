using Mawrid.Domain.Suppliers;

namespace Mawrid.Domain.Tests.Suppliers;

public class ValueObjectTests
{
    [Theory]
    [InlineData("300000000000003", true)]
    [InlineData(" 312345678900003 ", true)]
    [InlineData("200000000000003", false)]
    [InlineData("300000000000002", false)]
    [InlineData("30000000000003", false)]
    [InlineData("3000000000000A3", false)]
    [InlineData(null, false)]
    public void Vat_number_is_15_digits_starting_and_ending_with_3(string? value, bool valid)
    {
        VatNumber.Create(value).IsSuccess.Should().Be(valid);
    }

    [Theory]
    [InlineData("1010123456", true)]
    [InlineData("101012345", false)]
    [InlineData("10101234X6", false)]
    public void Cr_number_is_10_digits(string value, bool valid)
    {
        CrNumber.Create(value).IsSuccess.Should().Be(valid);
    }

    [Theory]
    [InlineData("SA0380000000608010167519")]
    [InlineData("sa03 8000 0000 6080 1016 7519")]
    [InlineData("SA0910000000000000000001")]
    public void Valid_saudi_iban_is_accepted_and_normalized(string value)
    {
        var iban = Iban.Create(value);

        iban.IsSuccess.Should().BeTrue();
        iban.Value.Value.Should().MatchRegex("^SA[0-9]{22}$");
    }

    [Theory]
    [InlineData("SA0480000000608010167519")] // wrong check digits
    [InlineData("AE070331234567890123456")] // not Saudi
    [InlineData("SA038000000060801016751")] // too short
    [InlineData("SA03800000006080101675!9")] // bad character
    [InlineData("")]
    public void Invalid_iban_is_rejected(string value)
    {
        Iban.Create(value).Error.Should().Be(SupplierErrors.IbanInvalid);
    }

    [Fact]
    public void Masked_iban_shows_only_the_last_four_characters()
    {
        Iban.Create("SA0380000000608010167519").Value.Masked.Should().Be("SA** **** **** **** **** 7519");
    }
}
