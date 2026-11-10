using Mawrid.Domain.Common;
using Mawrid.Domain.Payables;

namespace Mawrid.Domain.Tests.Payables;

/// <summary>
/// The PO in these tests: line 1 is 10 laptops at 3,900 SAR, line 2 is 10 docks at 500 SAR, all 15% VAT.
/// 6 laptops and 10 docks were accepted at the warehouse.
/// </summary>
public class InvoiceTests
{
    private const string SupplierVat = "310122393500003";
    private static readonly DateOnly Today = new(2026, 11, 10);
    private static readonly DateTimeOffset Now = new(2026, 11, 10, 9, 0, 0, TimeSpan.Zero);
    private static readonly Guid Clerk = Guid.NewGuid();

    private static MatchContext Context(bool pendingBank = false, decimal otherInvoicedLaptops = 0) => new(
        SupplierVat, pendingBank, PriceTolerancePercent: 2m, PriceToleranceMaxAmount: 500m,
        [new PoLineForMatch(1, 3_900m, 6m, otherInvoicedLaptops), new PoLineForMatch(2, 500m, 10m, 0m)]);

    /// <summary>A correct invoice for 6 laptops and 10 docks, with a matching QR code.</summary>
    private static SupplierInvoice Invoice(Func<InvoiceLineInput, InvoiceLineInput>? changeLaptops = null, string? qr = "auto", decimal? total = null)
    {
        var laptops = Line(1, "Laptop 14 inch", 6m, 3_900m);
        laptops = changeLaptops?.Invoke(laptops) ?? laptops;
        var lines = new[] { laptops, Line(2, "Docking station", 10m, 500m) };
        var net = lines.Sum(l => l.NetAmount);
        var vat = lines.Sum(l => l.VatAmount);
        var sum = total ?? net + vat;
        var qrText = qr == "auto" ? new ZatcaQr("Al Ufuq Supplies", SupplierVat, "2026-11-09T10:00:00Z", sum, vat).Encode() : qr;

        return SupplierInvoice.Receive(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(),
            new InvoiceDetails("INV-1001", Today.AddDays(-1), lines, net, vat, sum, qrText),
            InvoiceSource.UblXml, Guid.NewGuid(), null, Clerk, Today, Now).Value;
    }

    private static InvoiceLineInput Line(int poLine, string description, decimal quantity, decimal unitPrice)
    {
        var net = Money.Net(quantity, unitPrice);
        return new InvoiceLineInput(poLine, description, quantity, unitPrice, VatCategory.Standard, net, Money.Vat(net, 0.15m));
    }

    private static IReadOnlyList<string> Codes(SupplierInvoice invoice, MatchContext? context = null) =>
        InvoiceChecker.Check(invoice, context ?? Context()).Select(f => f.Code).ToList();

    [Fact]
    public void A_correct_invoice_has_no_findings_and_becomes_matched()
    {
        var invoice = Invoice();

        invoice.ApplyCheck(InvoiceChecker.Check(invoice, Context()), Now);

        invoice.Status.Should().Be(InvoiceStatus.Matched);
        invoice.Holds.Should().BeEmpty();
    }

    [Fact]
    public void A_missing_or_unreadable_qr_is_a_hold()
    {
        Codes(Invoice(qr: null)).Should().Equal(HoldCodes.QrMissing);
        Codes(Invoice(qr: "garbage")).Should().Equal(HoldCodes.QrMalformed);
    }

    [Fact]
    public void A_qr_from_another_vat_number_is_a_hold()
    {
        var foreign = new ZatcaQr("Someone else", "399999999900003", "2026-11-09T10:00:00Z", 32_200m, 4_200m).Encode();

        Codes(Invoice(qr: foreign)).Should().Contain(HoldCodes.QrVatMismatch);
    }

    [Fact]
    public void A_qr_total_that_differs_from_the_invoice_is_a_hold()
    {
        var wrongTotal = new ZatcaQr("Al Ufuq Supplies", SupplierVat, "2026-11-09T10:00:00Z", 30_000m, 4_200m).Encode();

        Codes(Invoice(qr: wrongTotal)).Should().Equal(HoldCodes.QrTotalMismatch);
    }

    [Fact]
    public void Wrong_line_vat_is_a_hold_but_one_halala_is_rounding()
    {
        Codes(Invoice(l => l with { VatAmount = l.VatAmount + 0.01m }, qr: null)).Should().Equal(HoldCodes.QrMissing);

        var findings = InvoiceChecker.Check(Invoice(l => l with { VatAmount = l.VatAmount + 5m }, qr: null), Context());

        findings.Should().ContainSingle(f => f.Code == HoldCodes.VatCalculation).Which.LineNumber.Should().Be(1);
    }

    [Fact]
    public void Totals_that_do_not_add_up_are_a_hold()
    {
        Codes(Invoice(total: 99_999m, qr: null)).Should().Contain(HoldCodes.TotalsMismatch);
    }

    [Fact]
    public void A_price_within_2_percent_passes_and_above_it_is_a_hold()
    {
        // 2% of 6 x 3,900 = 468 SAR may be added: 3,978 per laptop is the limit.
        Codes(Invoice(l => Line(1, l.Description, 6m, 3_978m))).Should().BeEmpty();
        Codes(Invoice(l => Line(1, l.Description, 6m, 3_980m))).Should().Contain(HoldCodes.MatchPrice);
    }

    [Fact]
    public void The_price_tolerance_is_capped_per_line()
    {
        // 2% of 10 x 3,900 would be 780 SAR, but the cap is 500 SAR: 3,950 passes, 3,951 does not.
        var context = Context() with { PoLines = [new PoLineForMatch(1, 3_900m, 10m, 0m), new PoLineForMatch(2, 500m, 10m, 0m)] };

        Codes(Invoice(l => Line(1, l.Description, 10m, 3_950m)), context).Should().BeEmpty();
        Codes(Invoice(l => Line(1, l.Description, 10m, 3_951m)), context).Should().Contain(HoldCodes.MatchPrice);
    }

    [Fact]
    public void Invoicing_more_than_was_accepted_is_a_hold()
    {
        Codes(Invoice(l => Line(1, l.Description, 7m, 3_900m))).Should().Contain(HoldCodes.MatchQuantity);
        Codes(Invoice(), Context(otherInvoicedLaptops: 1m)).Should().Contain(HoldCodes.MatchQuantity);
    }

    [Fact]
    public void An_unknown_po_line_and_a_pending_bank_change_are_holds()
    {
        Codes(Invoice(l => l with { PoLineNumber = 9 })).Should().Contain(HoldCodes.MatchLineUnknown);
        Codes(Invoice(), Context(pendingBank: true)).Should().Equal(HoldCodes.SupplierBankUnverified);
    }

    [Fact]
    public void An_override_clears_a_hold_and_survives_a_recheck_of_the_same_problem()
    {
        var invoice = Invoice(qr: null);
        invoice.ApplyCheck(InvoiceChecker.Check(invoice, Context()), Now);
        invoice.Status.Should().Be(InvoiceStatus.OnHold);

        invoice.Override(HoldCodes.QrMissing, null, "Paper invoice, QR checked by hand", Clerk, isAdmin: false, Now).IsSuccess.Should().BeTrue();
        invoice.Status.Should().Be(InvoiceStatus.Matched);

        invoice.ApplyCheck(InvoiceChecker.Check(invoice, Context()), Now);
        invoice.Status.Should().Be(InvoiceStatus.Matched);
        invoice.Holds.Should().ContainSingle().Which.OverrideReason.Should().Be("Paper invoice, QR checked by hand");
    }

    [Fact]
    public void Some_holds_cannot_be_overridden_and_price_needs_an_admin()
    {
        var invoice = Invoice(l => Line(1, l.Description, 7m, 4_100m));
        invoice.ApplyCheck(InvoiceChecker.Check(invoice, Context()), Now);

        invoice.Override(HoldCodes.MatchQuantity, 1, "We will receive the last laptop tomorrow", Clerk, isAdmin: true, Now)
            .Error.Should().Be(PayablesErrors.HoldNotOverridable);
        invoice.Override(HoldCodes.MatchPrice, 1, "Agreed price increase by phone", Clerk, isAdmin: false, Now)
            .Error.Should().Be(PayablesErrors.OverrideNeedsAdmin);
        invoice.Override(HoldCodes.MatchPrice, 1, "short", Clerk, isAdmin: true, Now).Error.Should().Be(PayablesErrors.ReasonRequired);
        invoice.Override(HoldCodes.MatchPrice, 1, "Agreed price increase by phone", Clerk, isAdmin: true, Now).IsSuccess.Should().BeTrue();

        invoice.Status.Should().Be(InvoiceStatus.OnHold, "the quantity hold is still open");
    }

    [Fact]
    public void Only_a_matched_invoice_can_be_released_and_it_reports_the_quantities()
    {
        var invoice = Invoice();
        invoice.Release(Clerk, Now).Error!.Code.Should().Be("invoice.invalid_status_change");

        invoice.ApplyCheck([], Now);
        invoice.Release(Clerk, Now).IsSuccess.Should().BeTrue();

        invoice.Status.Should().Be(InvoiceStatus.ReleasedForPayment);
        invoice.DomainEvents.OfType<SupplierInvoiceReleased>().Single().Quantities
            .Should().Equal(new InvoicedQuantity(1, 6m), new InvoicedQuantity(2, 10m));
    }

    [Fact]
    public void A_released_invoice_cannot_be_rejected_or_checked_again()
    {
        var invoice = Invoice();
        invoice.ApplyCheck([], Now);
        invoice.Release(Clerk, Now);

        invoice.Reject("Duplicate of an older invoice").IsFailure.Should().BeTrue();
        invoice.ApplyCheck([], Now).IsFailure.Should().BeTrue();
    }

    [Theory]
    [InlineData("", 1)]
    [InlineData("INV-1", 3)]
    public void Receive_validates_the_number_date_and_lines(string number, int daysAhead)
    {
        var line = Line(1, "Laptop", 1m, 10m);
        var details = new InvoiceDetails(number, Today.AddDays(daysAhead), [line], line.NetAmount, line.VatAmount, line.NetAmount + line.VatAmount, null);

        SupplierInvoice.Receive(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), details, InvoiceSource.Manual, null, null, Clerk, Today, Now)
            .Error.Should().Be(PayablesErrors.DetailsInvalid);
    }

    [Fact]
    public void Two_lines_for_the_same_po_line_are_refused()
    {
        var line = Line(1, "Laptop", 1m, 10m);
        var details = new InvoiceDetails("INV-2", Today, [line, line], 20m, 3m, 23m, null);

        SupplierInvoice.Receive(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), details, InvoiceSource.Manual, null, null, Clerk, Today, Now)
            .Error.Should().Be(PayablesErrors.LinesInvalid);
    }
}
