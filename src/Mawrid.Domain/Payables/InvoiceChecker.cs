using System.Globalization;
using Mawrid.Domain.Common;

namespace Mawrid.Domain.Payables;

/// <summary>
/// Checks an invoice: the ZATCA QR, the arithmetic, and the three-way match against the PO and the
/// goods received. Pure function: the caller loads the context, then stores the findings with
/// <see cref="SupplierInvoice.ApplyCheck"/>.
/// </summary>
public static class InvoiceChecker
{
    /// <summary>Differences up to one halala are rounding, not errors.</summary>
    public const decimal RoundingTolerance = 0.01m;

    public static IReadOnlyList<HoldFinding> Check(SupplierInvoice invoice, MatchContext context)
    {
        var findings = new List<HoldFinding>();
        CheckQr(invoice, context, findings);
        CheckArithmetic(invoice, findings);
        CheckMatch(invoice, context, findings);

        if (context.SupplierHasPendingBankChange)
        {
            findings.Add(new HoldFinding(HoldCodes.SupplierBankUnverified, null, "Verified bank account", "Bank change waiting for verification"));
        }

        return findings;
    }

    private static void CheckQr(SupplierInvoice invoice, MatchContext context, List<HoldFinding> findings)
    {
        if (invoice.QrCode is null)
        {
            findings.Add(new HoldFinding(HoldCodes.QrMissing, null, "ZATCA QR code", "None"));
            return;
        }

        var decoded = ZatcaQr.Decode(invoice.QrCode);
        if (decoded.IsFailure)
        {
            findings.Add(new HoldFinding(HoldCodes.QrMalformed, null, "Base64 TLV with tags 1–5", "Unreadable"));
            return;
        }

        var qr = decoded.Value;
        if (qr.VatNumber != context.SupplierVatNumber)
        {
            findings.Add(new HoldFinding(HoldCodes.QrVatMismatch, null, context.SupplierVatNumber, qr.VatNumber));
        }

        if (Differs(qr.Total, invoice.Total) || Differs(qr.VatTotal, invoice.VatTotal))
        {
            findings.Add(new HoldFinding(HoldCodes.QrTotalMismatch, null,
                $"total {Amount(invoice.Total)}, VAT {Amount(invoice.VatTotal)}", $"total {Amount(qr.Total)}, VAT {Amount(qr.VatTotal)}"));
        }
    }

    private static void CheckArithmetic(SupplierInvoice invoice, List<HoldFinding> findings)
    {
        foreach (var line in invoice.Lines)
        {
            var net = Money.Net(line.Quantity, line.UnitPrice);
            if (Differs(line.NetAmount, net))
            {
                findings.Add(new HoldFinding(HoldCodes.LineAmount, line.PoLineNumber, Amount(net), Amount(line.NetAmount)));
            }

            var vat = Money.Vat(line.NetAmount, VatRates.RateFor(line.VatCategory, invoice.IssueDate));
            if (Differs(line.VatAmount, vat))
            {
                findings.Add(new HoldFinding(HoldCodes.VatCalculation, line.PoLineNumber, Amount(vat), Amount(line.VatAmount)));
            }
        }

        var netSum = invoice.Lines.Sum(l => l.NetAmount);
        var vatSum = invoice.Lines.Sum(l => l.VatAmount);
        if (Differs(invoice.NetTotal, netSum) || Differs(invoice.VatTotal, vatSum) || Differs(invoice.Total, invoice.NetTotal + invoice.VatTotal))
        {
            findings.Add(new HoldFinding(HoldCodes.TotalsMismatch, null,
                $"net {Amount(netSum)}, VAT {Amount(vatSum)}, total {Amount(invoice.NetTotal + invoice.VatTotal)}",
                $"net {Amount(invoice.NetTotal)}, VAT {Amount(invoice.VatTotal)}, total {Amount(invoice.Total)}"));
        }
    }

    private static void CheckMatch(SupplierInvoice invoice, MatchContext context, List<HoldFinding> findings)
    {
        foreach (var line in invoice.Lines)
        {
            var poLine = context.PoLines.SingleOrDefault(l => l.LineNumber == line.PoLineNumber);
            if (poLine is null)
            {
                findings.Add(new HoldFinding(HoldCodes.MatchLineUnknown, line.PoLineNumber, "A line of the PO", "No such line"));
                continue;
            }

            // Price: the extra cost on this line may not exceed the percentage, and never the cap in SAR.
            var extra = (line.UnitPrice - poLine.UnitPrice) * line.Quantity;
            var allowed = Math.Min(Money.Round(poLine.UnitPrice * line.Quantity * context.PriceTolerancePercent / 100m), context.PriceToleranceMaxAmount);
            if (extra > allowed + RoundingTolerance)
            {
                findings.Add(new HoldFinding(HoldCodes.MatchPrice, line.PoLineNumber, Amount(poLine.UnitPrice), Amount(line.UnitPrice)));
            }

            // Quantity: everything invoiced so far may not exceed what was accepted at the warehouse.
            var available = poLine.AcceptedQuantity - poLine.OtherInvoicedQuantity;
            if (line.Quantity > available)
            {
                findings.Add(new HoldFinding(HoldCodes.MatchQuantity, line.PoLineNumber, Quantity(Math.Max(available, 0)), Quantity(line.Quantity)));
            }
        }
    }

    private static bool Differs(decimal a, decimal b) => Math.Abs(a - b) > RoundingTolerance;

    private static string Amount(decimal value) => value.ToString("0.00##", CultureInfo.InvariantCulture);

    private static string Quantity(decimal value) => value.ToString("0.###", CultureInfo.InvariantCulture);
}

/// <summary>What the checker needs to know besides the invoice.</summary>
public sealed record MatchContext(
    string SupplierVatNumber,
    bool SupplierHasPendingBankChange,
    decimal PriceTolerancePercent,
    decimal PriceToleranceMaxAmount,
    IReadOnlyList<PoLineForMatch> PoLines);

/// <param name="OtherInvoicedQuantity">Quantity on this supplier's other invoices for the line, except rejected ones.</param>
public sealed record PoLineForMatch(int LineNumber, decimal UnitPrice, decimal AcceptedQuantity, decimal OtherInvoicedQuantity);
