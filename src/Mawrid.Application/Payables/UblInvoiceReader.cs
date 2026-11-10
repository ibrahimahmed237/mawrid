using System.Globalization;
using System.Xml;
using System.Xml.Linq;
using Mawrid.Domain.Common;
using Mawrid.Domain.Payables;

namespace Mawrid.Application.Payables;

/// <summary>
/// Reads the fields we need from a ZATCA UBL 2.1 invoice. It does not validate the full ZATCA schema
/// or the signature (DESIGN section 14): it reads what the three-way match needs.
/// </summary>
public static class UblInvoiceReader
{
    private static readonly XNamespace Cac = "urn:oasis:names:specification:ubl:schema:xsd:CommonAggregateComponents-2";
    private static readonly XNamespace Cbc = "urn:oasis:names:specification:ubl:schema:xsd:CommonBasicComponents-2";
    private static readonly XNamespace InvoiceNs = "urn:oasis:names:specification:ubl:schema:xsd:Invoice-2";

    /// <summary>No DTDs and no external entities: protects against XXE and "billion laughs" attacks.</summary>
    private static readonly XmlReaderSettings SafeSettings = new()
    {
        DtdProcessing = DtdProcessing.Prohibit,
        XmlResolver = null,
        MaxCharactersInDocument = 10_000_000,
    };

    public static Result<UblInvoice> Read(Stream xml)
    {
        XDocument document;
        try
        {
            using var reader = XmlReader.Create(xml, SafeSettings);
            document = XDocument.Load(reader);
        }
        catch (XmlException)
        {
            return PayablesErrors.SourceFileInvalid;
        }

        var root = document.Root;
        if (root is null || root.Name != InvoiceNs + "Invoice")
        {
            return PayablesErrors.SourceFileInvalid;
        }

        try
        {
            var lines = root.Elements(Cac + "InvoiceLine").Select(ReadLine).ToList();
            var totals = root.Element(Cac + "LegalMonetaryTotal");
            var details = new InvoiceDetails(
                Text(root, Cbc + "ID"),
                DateOnly.ParseExact(Text(root, Cbc + "IssueDate"), "yyyy-MM-dd", CultureInfo.InvariantCulture),
                lines,
                Amount(totals, Cbc + "TaxExclusiveAmount"),
                Amount(root.Element(Cac + "TaxTotal"), Cbc + "TaxAmount"),
                Amount(totals, Cbc + "TaxInclusiveAmount"),
                root.Elements(Cac + "AdditionalDocumentReference")
                    .FirstOrDefault(r => (string?)r.Element(Cbc + "ID") == "QR")?
                    .Element(Cac + "Attachment")?.Element(Cbc + "EmbeddedDocumentBinaryObject")?.Value.Trim());

            var sellerVat = root.Element(Cac + "AccountingSupplierParty")?.Element(Cac + "Party")?
                .Element(Cac + "PartyTaxScheme")?.Element(Cbc + "CompanyID")?.Value.Trim();

            return new UblInvoice(Text(root.Element(Cac + "OrderReference"), Cbc + "ID"), sellerVat, details);
        }
        catch (Exception exception) when (exception is FormatException or InvalidDataException or OverflowException)
        {
            return PayablesErrors.SourceFileInvalid;
        }
    }

    private static InvoiceLineInput ReadLine(XElement line)
    {
        var item = line.Element(Cac + "Item");
        var orderLine = line.Element(Cac + "OrderLineReference")?.Element(Cbc + "LineID")?.Value ?? Text(line, Cbc + "ID");
        return new InvoiceLineInput(
            int.Parse(orderLine, NumberStyles.Integer, CultureInfo.InvariantCulture),
            Text(item, Cbc + "Name"),
            Amount(line, Cbc + "InvoicedQuantity"),
            Amount(line.Element(Cac + "Price"), Cbc + "PriceAmount"),
            Category(item?.Element(Cac + "ClassifiedTaxCategory")?.Element(Cbc + "ID")?.Value),
            Amount(line, Cbc + "LineExtensionAmount"),
            Amount(line.Element(Cac + "TaxTotal"), Cbc + "TaxAmount"));
    }

    /// <summary>UNCL5305 codes used by ZATCA: S standard, Z zero-rated, E exempt, O out of scope.</summary>
    private static VatCategory Category(string? code) => code?.Trim() switch
    {
        "S" => VatCategory.Standard,
        "Z" => VatCategory.ZeroRated,
        "E" or "O" => VatCategory.Exempt,
        _ => throw new InvalidDataException($"Unknown VAT category '{code}'."),
    };

    private static string Text(XElement? parent, XName name)
    {
        var value = parent?.Element(name)?.Value.Trim();
        return string.IsNullOrEmpty(value) ? throw new InvalidDataException($"Missing {name.LocalName}.") : value;
    }

    private static decimal Amount(XElement? parent, XName name) =>
        decimal.Parse(Text(parent, name), NumberStyles.AllowDecimalPoint | NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture);
}

/// <param name="PurchaseOrderNumber">From cac:OrderReference, for example PO-2026-000012.</param>
/// <param name="SellerVatNumber">The supplier's VAT number as written in the XML.</param>
public sealed record UblInvoice(string PurchaseOrderNumber, string? SellerVatNumber, InvoiceDetails Details);
