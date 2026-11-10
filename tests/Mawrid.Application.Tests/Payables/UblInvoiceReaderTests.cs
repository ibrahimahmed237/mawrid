using System.Text;
using Mawrid.Application.Payables;
using Mawrid.Domain.Common;
using Mawrid.Domain.Payables;

namespace Mawrid.Application.Tests.Payables;

public class UblInvoiceReaderTests
{
    private const string Sample = """
        <?xml version="1.0" encoding="UTF-8"?>
        <Invoice xmlns="urn:oasis:names:specification:ubl:schema:xsd:Invoice-2"
                 xmlns:cac="urn:oasis:names:specification:ubl:schema:xsd:CommonAggregateComponents-2"
                 xmlns:cbc="urn:oasis:names:specification:ubl:schema:xsd:CommonBasicComponents-2">
          <cbc:ID>INV-1001</cbc:ID>
          <cbc:IssueDate>2026-11-09</cbc:IssueDate>
          <cac:OrderReference><cbc:ID>PO-2026-000012</cbc:ID></cac:OrderReference>
          <cac:AdditionalDocumentReference>
            <cbc:ID>QR</cbc:ID>
            <cac:Attachment><cbc:EmbeddedDocumentBinaryObject mimeCode="text/plain">QR-TEXT</cbc:EmbeddedDocumentBinaryObject></cac:Attachment>
          </cac:AdditionalDocumentReference>
          <cac:AccountingSupplierParty><cac:Party><cac:PartyTaxScheme><cbc:CompanyID>310122393500003</cbc:CompanyID></cac:PartyTaxScheme></cac:Party></cac:AccountingSupplierParty>
          <cac:TaxTotal><cbc:TaxAmount currencyID="SAR">150.00</cbc:TaxAmount></cac:TaxTotal>
          <cac:LegalMonetaryTotal>
            <cbc:TaxExclusiveAmount currencyID="SAR">1000.00</cbc:TaxExclusiveAmount>
            <cbc:TaxInclusiveAmount currencyID="SAR">1150.00</cbc:TaxInclusiveAmount>
          </cac:LegalMonetaryTotal>
          <cac:InvoiceLine>
            <cbc:ID>1</cbc:ID>
            <cbc:InvoicedQuantity unitCode="PCE">2</cbc:InvoicedQuantity>
            <cbc:LineExtensionAmount currencyID="SAR">1000.00</cbc:LineExtensionAmount>
            <cac:OrderLineReference><cbc:LineID>3</cbc:LineID></cac:OrderLineReference>
            <cac:TaxTotal><cbc:TaxAmount currencyID="SAR">150.00</cbc:TaxAmount></cac:TaxTotal>
            <cac:Item>
              <cbc:Name>Office chair</cbc:Name>
              <cac:ClassifiedTaxCategory><cbc:ID>S</cbc:ID><cbc:Percent>15</cbc:Percent></cac:ClassifiedTaxCategory>
            </cac:Item>
            <cac:Price><cbc:PriceAmount currencyID="SAR">500.00</cbc:PriceAmount></cac:Price>
          </cac:InvoiceLine>
        </Invoice>
        """;

    private static Result<UblInvoice> Read(string xml) => UblInvoiceReader.Read(new MemoryStream(Encoding.UTF8.GetBytes(xml)));

    [Fact]
    public void Reads_the_fields_the_match_needs()
    {
        var invoice = Read(Sample).Value;

        invoice.PurchaseOrderNumber.Should().Be("PO-2026-000012");
        invoice.SellerVatNumber.Should().Be("310122393500003");
        invoice.Details.Should().BeEquivalentTo(new
        {
            SupplierInvoiceNumber = "INV-1001",
            IssueDate = new DateOnly(2026, 11, 9),
            NetTotal = 1000m,
            VatTotal = 150m,
            Total = 1150m,
            QrCode = "QR-TEXT",
        });
        invoice.Details.Lines.Should().ContainSingle().Which.Should().Be(
            new InvoiceLineInput(3, "Office chair", 2m, 500m, VatCategory.Standard, 1000m, 150m));
    }

    [Fact]
    public void Another_root_element_is_refused()
    {
        Read("<CreditNote xmlns=\"urn:oasis:names:specification:ubl:schema:xsd:CreditNote-2\"/>").Error.Should().Be(PayablesErrors.SourceFileInvalid);
    }

    [Fact]
    public void A_doctype_is_refused_so_external_entities_never_load()
    {
        var withDoctype = Sample.Replace("<Invoice ", "<!DOCTYPE Invoice [<!ENTITY x SYSTEM \"file:///etc/passwd\">]>\n<Invoice ", StringComparison.Ordinal);

        Read(withDoctype).Error.Should().Be(PayablesErrors.SourceFileInvalid);
    }

    [Theory]
    [InlineData("<cbc:ID>INV-1001</cbc:ID>", "")]
    [InlineData("<cbc:ID>S</cbc:ID>", "<cbc:ID>X</cbc:ID>")]
    [InlineData("<cbc:IssueDate>2026-11-09</cbc:IssueDate>", "<cbc:IssueDate>09/11/2026</cbc:IssueDate>")]
    public void Missing_or_bad_fields_are_refused(string from, string to)
    {
        Read(Sample.Replace(from, to, StringComparison.Ordinal)).Error.Should().Be(PayablesErrors.SourceFileInvalid);
    }

    [Fact]
    public void Text_that_is_not_xml_is_refused()
    {
        Read("%PDF-1.7 not xml").Error.Should().Be(PayablesErrors.SourceFileInvalid);
    }

    [Fact]
    public void The_sample_in_the_docs_is_a_valid_matching_invoice()
    {
        var root = new DirectoryInfo(AppContext.BaseDirectory);
        while (!File.Exists(Path.Combine(root.FullName, "Mawrid.slnx")))
        {
            root = root.Parent!;
        }

        using var file = File.OpenRead(Path.Combine(root.FullName, "docs", "samples", "ubl-invoice.xml"));
        var invoice = UblInvoiceReader.Read(file).Value;
        var qr = ZatcaQr.Decode(invoice.Details.QrCode).Value;

        invoice.Details.Lines.Should().HaveCount(2);
        qr.Total.Should().Be(invoice.Details.Total);
        qr.VatTotal.Should().Be(invoice.Details.VatTotal);
        invoice.Details.Lines.Sum(l => l.NetAmount).Should().Be(invoice.Details.NetTotal);
    }
}
