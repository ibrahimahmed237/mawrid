using Mawrid.Domain.Common;
using Mawrid.Domain.Sourcing;

namespace Mawrid.Domain.Tests.Sourcing;

public class RfqTests
{
    private static readonly DateTimeOffset Now = new(2026, 11, 10, 9, 0, 0, TimeSpan.Zero);
    private static readonly DateOnly Today = new(2026, 11, 10);
    private static readonly DateTimeOffset Deadline = Now.AddDays(5);
    private static readonly Guid SupplierA = Guid.NewGuid();
    private static readonly Guid SupplierB = Guid.NewGuid();
    private static readonly Guid SupplierC = Guid.NewGuid();

    private static Rfq Draft() => Rfq.CreateDraft(Guid.NewGuid(), "RFQ-2026-000001", Guid.NewGuid(), Guid.NewGuid(), "Laptops", null,
    [
        new RfqItemInput("Laptop 14 inch", 10, "pcs"),
        new RfqItemInput("Docking station", 10, "pcs"),
    ]).Value;

    private static Rfq Published()
    {
        var rfq = Draft();
        rfq.Invite([SupplierA, SupplierB, SupplierC], Now);
        rfq.SetDeadline(Deadline);
        rfq.Publish(Now, 3, 3, null).IsSuccess.Should().BeTrue();
        return rfq;
    }

    private static QuotationInput Quote(decimal laptopPrice, decimal dockPrice) => new(Today.AddDays(30),
    [
        new QuotationLineInput(1, laptopPrice, VatCategory.Standard, 7),
        new QuotationLineInput(2, dockPrice, VatCategory.Standard, 7),
    ], null, []);

    [Fact]
    public void Publish_needs_invitations_and_a_deadline_after_the_minimum_window()
    {
        var rfq = Draft();

        rfq.Publish(Now, 3, 3, null).Error.Should().Be(SourcingErrors.NoInvitations);
        rfq.Invite([SupplierA, SupplierB, SupplierC], Now);
        rfq.SetDeadline(Now.AddDays(2));
        rfq.Publish(Now, 3, 3, null).Error!.Code.Should().Be("rfq.deadline_too_soon");
        rfq.SetDeadline(Deadline);
        rfq.Publish(Now, 3, 3, null).IsSuccess.Should().BeTrue();
        rfq.DomainEvents.OfType<RfqPublished>().Single().InvitedSupplierIds.Should().HaveCount(3);
    }

    [Fact]
    public void Fewer_suppliers_than_the_minimum_needs_a_single_source_reason()
    {
        var rfq = Draft();
        rfq.Invite([SupplierA], Now);
        rfq.SetDeadline(Deadline);

        rfq.Publish(Now, 3, 3, null).Error!.Code.Should().Be("rfq.single_source_reason_required");
        rfq.Publish(Now, 3, 3, "Only authorized distributor in KSA").IsSuccess.Should().BeTrue();
        rfq.SingleSourceReason.Should().Be("Only authorized distributor in KSA");
    }

    [Fact]
    public void Quotations_are_accepted_until_the_deadline_even_if_no_job_closed_the_rfq()
    {
        var rfq = Published();

        Quotation.Submit(rfq, SupplierA, Quote(4_000, 500), Deadline.AddSeconds(-1), Today).IsSuccess.Should().BeTrue();
        Quotation.Submit(rfq, SupplierB, Quote(3_900, 450), Deadline, Today).Error.Should().Be(SourcingErrors.NotOpen);
        rfq.Status.Should().Be(RfqStatus.Published, "nothing closed it, but the deadline still applies");
    }

    [Fact]
    public void Only_invited_suppliers_can_quote()
    {
        Quotation.Submit(Published(), Guid.NewGuid(), Quote(4_000, 500), Now, Today).Error.Should().Be(SourcingErrors.NotInvited);
    }

    [Fact]
    public void Quotations_are_sealed_until_the_deadline()
    {
        var rfq = Published();

        rfq.IsSealed(Now).Should().BeTrue();
        rfq.IsSealed(Deadline).Should().BeFalse();
        rfq.Close(Now).Error.Should().Be(SourcingErrors.DeadlineNotPassed);
        rfq.Close(Deadline).IsSuccess.Should().BeTrue();
        rfq.IsSealed(Now).Should().BeFalse("a closed RFQ is no longer sealed");
    }

    [Fact]
    public void A_revision_keeps_one_quotation_and_counts_versions()
    {
        var rfq = Published();
        var quotation = Quotation.Submit(rfq, SupplierA, Quote(4_000, 500), Now, Today).Value;

        quotation.Revise(rfq, Quote(3_800, 480), Now.AddHours(1), Today).IsSuccess.Should().BeTrue();

        quotation.Version.Should().Be(2);
        quotation.Lines.Single(l => l.ItemLineNumber == 1).UnitPrice.Should().Be(3_800);
    }

    [Fact]
    public void An_amendment_asks_earlier_quotations_to_reconfirm()
    {
        var rfq = Published();
        var quotation = Quotation.Submit(rfq, SupplierA, Quote(4_000, 500), Now, Today).Value;

        rfq.Amend(Now.AddDays(1), Now.AddDays(1), new Dictionary<int, decimal> { [1] = 12 }, 3).Error!.Code.Should().Be("rfq.deadline_too_soon");
        rfq.Amend(Now.AddDays(1), Now.AddDays(6), new Dictionary<int, decimal> { [1] = 12 }, 3).IsSuccess.Should().BeTrue();

        rfq.Revision.Should().Be(1);
        rfq.Items[0].Quantity.Should().Be(12);
        quotation.NeedsReconfirmation(rfq).Should().BeTrue();
        quotation.Revise(rfq, Quote(4_000, 500), Now.AddDays(2), Today);
        quotation.NeedsReconfirmation(rfq).Should().BeFalse();
    }

    [Fact]
    public void Award_closes_the_rfq_and_needs_a_justification_when_not_the_lowest_price()
    {
        var rfq = Published();
        var qa = Guid.NewGuid();
        var qb = Guid.NewGuid();

        rfq.Award([new AwardDecision(1, SupplierA, qa, 4_000, VatCategory.Standard, true, null)], Now).Error.Should().Be(SourcingErrors.DeadlineNotPassed);

        var notLowest = rfq.Award([new AwardDecision(1, SupplierB, qb, 4_100, VatCategory.Standard, false, "short")], Deadline);
        notLowest.Error!.Code.Should().Be("rfq.justification_required");

        var award = rfq.Award(
        [
            new AwardDecision(1, SupplierA, qa, 4_000, VatCategory.Standard, true, null),
            new AwardDecision(2, SupplierB, qb, 520, VatCategory.Standard, false, "Supplier B delivers 3 weeks earlier"),
        ], Deadline);

        award.IsSuccess.Should().BeTrue();
        rfq.Status.Should().Be(RfqStatus.Awarded);
        rfq.Awards.Should().HaveCount(2);
        rfq.DomainEvents.OfType<RfqAwarded>().Single().WinningSupplierIds.Should().BeEquivalentTo([SupplierA, SupplierB]);
        rfq.Award([new AwardDecision(1, SupplierA, qa, 4_000, VatCategory.Standard, true, null)], Deadline).Error.Should().Be(SourcingErrors.AlreadyAwarded);
    }

    [Fact]
    public void An_item_can_be_awarded_only_once()
    {
        var rfq = Published();
        var q = Guid.NewGuid();

        rfq.Award(
        [
            new AwardDecision(1, SupplierA, q, 4_000, VatCategory.Standard, true, null),
            new AwardDecision(1, SupplierB, q, 4_100, VatCategory.Standard, false, "second award for same item"),
        ], Deadline).Error.Should().Be(SourcingErrors.AwardInvalid);
    }

    [Fact]
    public void The_first_opening_of_quotations_is_recorded_once()
    {
        var rfq = Published();
        var buyer = Guid.NewGuid();

        rfq.RecordQuotationsOpened(buyer, Deadline);
        rfq.RecordQuotationsOpened(Guid.NewGuid(), Deadline.AddHours(1));

        rfq.QuotationsOpenedBy.Should().Be(buyer);
        rfq.QuotationsOpenedAtUtc.Should().Be(Deadline);
    }
}
