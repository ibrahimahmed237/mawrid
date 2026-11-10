using System.Globalization;
using System.Xml.Linq;
using Mawrid.Domain.Common;
using Mawrid.Domain.Payables;

namespace Mawrid.Api.Tests.Infrastructure;

/// <summary>Writes a small ZATCA-style UBL 2.1 invoice, like a supplier's e-invoicing system would.</summary>
public sealed class UblInvoiceBuilder(string invoiceNumber, string purchaseOrderNumber, string sellerVatNumber)
{
    private static readonly XNamespace Inv = "urn:oasis:names:specification:ubl:schema:xsd:Invoice-2";
    private static readonly XNamespace Cac = "urn:oasis:names:specification:ubl:schema:xsd:CommonAggregateComponents-2";
    private static readonly XNamespace Cbc = "urn:oasis:names:specification:ubl:schema:xsd:CommonBasicComponents-2";

    private readonly List<(int PoLine, string Name, decimal Quantity, decimal Price)> _lines = [];

    public DateOnly IssueDate { get; set; } = DateOnly.FromDateTime(DateTime.UtcNow);

    /// <summary>Set to change the QR: null leaves it out, a value replaces the correct one.</summary>
    public string? QrOverride { get; set; } = "auto";

    public UblInvoiceBuilder Line(int poLine, string name, decimal quantity, decimal unitPrice)
    {
        _lines.Add((poLine, name, quantity, unitPrice));
        return this;
    }

    public byte[] Build()
    {
        var lines = _lines.Select(l =>
        {
            var net = Money.Net(l.Quantity, l.Price);
            return (l.PoLine, l.Name, l.Quantity, l.Price, Net: net, Vat: Money.Vat(net, 0.15m));
        }).ToList();
        var net = lines.Sum(l => l.Net);
        var vat = lines.Sum(l => l.Vat);
        var qr = QrOverride == "auto"
            ? new ZatcaQr("Al Ufuq Supplies", sellerVatNumber, $"{IssueDate:yyyy-MM-dd}T10:00:00Z", net + vat, vat).Encode()
            : QrOverride;

        var invoice = new XElement(Inv + "Invoice",
            new XAttribute(XNamespace.Xmlns + "cac", Cac),
            new XAttribute(XNamespace.Xmlns + "cbc", Cbc),
            new XElement(Cbc + "ProfileID", "reporting:1.0"),
            new XElement(Cbc + "ID", invoiceNumber),
            new XElement(Cbc + "IssueDate", IssueDate.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)),
            new XElement(Cbc + "DocumentCurrencyCode", "SAR"),
            new XElement(Cac + "OrderReference", new XElement(Cbc + "ID", purchaseOrderNumber)),
            qr is null ? null : new XElement(Cac + "AdditionalDocumentReference",
                new XElement(Cbc + "ID", "QR"),
                new XElement(Cac + "Attachment", new XElement(Cbc + "EmbeddedDocumentBinaryObject", new XAttribute("mimeCode", "text/plain"), qr))),
            new XElement(Cac + "AccountingSupplierParty", new XElement(Cac + "Party",
                new XElement(Cac + "PartyTaxScheme", new XElement(Cbc + "CompanyID", sellerVatNumber)))),
            new XElement(Cac + "TaxTotal", Amount(Cbc + "TaxAmount", vat)),
            new XElement(Cac + "LegalMonetaryTotal",
                Amount(Cbc + "LineExtensionAmount", net),
                Amount(Cbc + "TaxExclusiveAmount", net),
                Amount(Cbc + "TaxInclusiveAmount", net + vat),
                Amount(Cbc + "PayableAmount", net + vat)),
            lines.Select((l, i) => new XElement(Cac + "InvoiceLine",
                new XElement(Cbc + "ID", i + 1),
                new XElement(Cbc + "InvoicedQuantity", new XAttribute("unitCode", "PCE"), l.Quantity.ToString(CultureInfo.InvariantCulture)),
                Amount(Cbc + "LineExtensionAmount", l.Net),
                new XElement(Cac + "OrderLineReference", new XElement(Cbc + "LineID", l.PoLine)),
                new XElement(Cac + "TaxTotal", Amount(Cbc + "TaxAmount", l.Vat)),
                new XElement(Cac + "Item",
                    new XElement(Cbc + "Name", l.Name),
                    new XElement(Cac + "ClassifiedTaxCategory", new XElement(Cbc + "ID", "S"), new XElement(Cbc + "Percent", "15"))),
                new XElement(Cac + "Price", Amount(Cbc + "PriceAmount", l.Price)))));

        using var stream = new MemoryStream();
        new XDocument(new XDeclaration("1.0", "UTF-8", null), invoice).Save(stream);
        return stream.ToArray();
    }

    private static XElement Amount(XName name, decimal amount) =>
        new(name, new XAttribute("currencyID", "SAR"), amount.ToString("0.00##", CultureInfo.InvariantCulture));
}
