using Mawrid.Domain.Common;
using Mawrid.Domain.Purchasing;

namespace Mawrid.Domain.Tests.Purchasing;

public class PurchaseOrderTests
{
    private static readonly DateOnly Today = new(2026, 11, 10);
    private static readonly DateTimeOffset Now = new(2026, 11, 10, 9, 0, 0, TimeSpan.Zero);

    private static PurchaseOrder Draft() => PurchaseOrder.CreateDraft(Guid.NewGuid(), "PO-2026-000001", Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(),
        PurchaseOrderSource.Rfq, Guid.NewGuid(), [],
        [
            new PurchaseOrderLineInput("Laptop", 10, "pcs", 3_999.99m, VatCategory.Standard),
            new PurchaseOrderLineInput("Medical masks", 100, "box", 12.345m, VatCategory.ZeroRated),
        ], Today).Value;

    private static PurchaseOrder Sent()
    {
        var po = Draft();
        po.Submit(Guid.NewGuid());
        po.MarkApproved();
        po.Send(Now);
        return po;
    }

    [Fact]
    public void Totals_are_calculated_per_line_with_vat_rounded_half_away_from_zero()
    {
        var po = Draft();

        po.Lines[0].NetAmount.Should().Be(39_999.90m);
        po.Lines[0].VatAmount.Should().Be(5_999.99m); // 5999.985 rounds away from zero
        po.Lines[1].NetAmount.Should().Be(1_234.50m);
        po.Lines[1].VatAmount.Should().Be(0m);
        po.Total.Should().Be(39_999.90m + 5_999.99m + 1_234.50m);
    }

    [Theory]
    [InlineData(2020, 6, 30, 0.05)]
    [InlineData(2020, 7, 1, 0.15)]
    [InlineData(2026, 11, 10, 0.15)]
    public void The_vat_rate_depends_on_the_date(int year, int month, int day, decimal expected)
    {
        VatRates.RateFor(VatCategory.Standard, new DateOnly(year, month, day)).Should().Be(expected);
        VatRates.RateFor(VatCategory.Exempt, new DateOnly(year, month, day)).Should().Be(0);
    }

    [Fact]
    public void A_rejected_po_goes_back_to_draft_with_the_reason()
    {
        var po = Draft();
        po.Submit(Guid.NewGuid());

        po.MarkRejected("Negotiate a better price").IsSuccess.Should().BeTrue();

        po.Status.Should().Be(PurchaseOrderStatus.Draft);
        po.RejectionReason.Should().Be("Negotiate a better price");
        po.ApprovalRequestId.Should().BeNull();
        po.Submit(Guid.NewGuid()).IsSuccess.Should().BeTrue();
    }

    [Fact]
    public void Only_an_approved_po_can_be_sent()
    {
        var po = Draft();

        po.Send(Now).Error!.Code.Should().Be("purchase_order.invalid_status_change");
    }

    [Fact]
    public void Partial_then_full_receipt()
    {
        var po = Sent();
        var receiver = Guid.NewGuid();

        po.RecordReceipt(receiver, [new ReceiptLineInput(1, 6, 1, "Damaged screen")], 0, null, Now).IsSuccess.Should().BeTrue();
        po.Status.Should().Be(PurchaseOrderStatus.PartiallyReceived);

        po.RecordReceipt(receiver, [new ReceiptLineInput(1, 4), new ReceiptLineInput(2, 100)], 0, "Second delivery", Now.AddDays(2)).IsSuccess.Should().BeTrue();

        po.Status.Should().Be(PurchaseOrderStatus.Received);
        po.Lines[0].AcceptedQuantity.Should().Be(10);
        po.Lines[0].RejectedQuantity.Should().Be(1);
        po.Receipts.Should().HaveCount(2);
    }

    [Fact]
    public void Receiving_more_than_ordered_is_refused_unless_the_tolerance_allows_it()
    {
        var po = Sent();

        po.RecordReceipt(Guid.NewGuid(), [new ReceiptLineInput(2, 105)], 0, null, Now).Error!.Code.Should().Be("receipt.over_delivery");
        po.RecordReceipt(Guid.NewGuid(), [new ReceiptLineInput(2, 105)], 5, null, Now).IsSuccess.Should().BeTrue();
    }

    [Fact]
    public void A_rejected_quantity_needs_a_reason()
    {
        Sent().RecordReceipt(Guid.NewGuid(), [new ReceiptLineInput(1, 5, 1)], 0, null, Now).Error.Should().Be(PurchasingErrors.RejectionReasonRequired);
    }

    [Fact]
    public void A_po_cannot_be_cancelled_after_a_receipt_and_closes_when_fully_invoiced()
    {
        var po = Sent();
        po.RecordReceipt(Guid.NewGuid(), [new ReceiptLineInput(1, 10), new ReceiptLineInput(2, 100)], 0, null, Now);

        po.Cancel("Changed our mind").Error.Should().Be(PurchasingErrors.CannotCancel);
        po.Close().Error.Should().Be(PurchasingErrors.CannotClose);

        po.RecordInvoiced(1, 10);
        po.RecordInvoiced(2, 100);
        po.Close().IsSuccess.Should().BeTrue();
    }

    [Fact]
    public void Goods_can_be_received_only_after_sending()
    {
        Draft().RecordReceipt(Guid.NewGuid(), [new ReceiptLineInput(1, 1)], 0, null, Now).Error.Should().Be(PurchasingErrors.NotReceivable);
    }

    [Fact]
    public void The_buyer_cannot_receive_their_own_order()
    {
        var po = Sent();

        po.RecordReceipt(po.BuyerId, [new ReceiptLineInput(1, 1)], 0, null, Now).Error.Should().Be(PurchasingErrors.ReceiverIsBuyer);
    }

    [Fact]
    public void A_partly_received_po_can_be_short_closed_with_a_reason()
    {
        var po = Sent();
        po.RecordReceipt(Guid.NewGuid(), [new ReceiptLineInput(1, 4)], 0, null, Now);

        po.Close().Error.Should().Be(PurchasingErrors.ShortCloseReasonRequired);
        po.Close("Supplier discontinued the model").IsSuccess.Should().BeTrue();

        po.Status.Should().Be(PurchaseOrderStatus.Closed);
        po.ClosedReason.Should().Be("Supplier discontinued the model");
    }

    [Fact]
    public void A_po_with_nothing_received_cannot_be_closed()
    {
        Sent().Close("Not needed any more").Error.Should().Be(PurchasingErrors.CannotClose);
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(181)]
    public void Payment_terms_must_be_in_range(int days)
    {
        Draft().UpdateTerms(days, null).Error.Should().Be(PurchasingErrors.TermsInvalid);
    }

    [Fact]
    public void Terms_can_change_only_in_draft()
    {
        var po = Draft();
        po.UpdateTerms(60, "  Riyadh warehouse, gate 3 ").IsSuccess.Should().BeTrue();
        po.PaymentTermsDays.Should().Be(60);
        po.DeliveryAddress.Should().Be("Riyadh warehouse, gate 3");

        Sent().UpdateTerms(45, null).Error.Should().Be(PurchasingErrors.NotDraft);
    }
}
