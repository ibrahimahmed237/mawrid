using Mawrid.Domain.Identity;
using Mawrid.Domain.Requisitions;

namespace Mawrid.Domain.Tests.Requisitions;

public class PurchaseRequisitionTests
{
    private static readonly DateOnly Today = new(2026, 11, 10);

    private static RequisitionDetails Details(params RequisitionLineInput[] lines) => new(
        "Laptops for new hires", "Three engineers start in December and need laptops.", Today.AddDays(30),
        lines.Length > 0 ? lines : [new RequisitionLineInput("Laptop 14 inch, 32 GB RAM", 3, "pcs", 4_500m)]);

    private static PurchaseRequisition Draft(params RequisitionLineInput[] lines) =>
        PurchaseRequisition.CreateDraft(Guid.NewGuid(), "PR-2026-000001", Guid.NewGuid(), Guid.NewGuid(), Details(lines), Today).Value;

    [Fact]
    public void Totals_are_calculated_from_the_lines()
    {
        var requisition = Draft(
            new RequisitionLineInput("Laptop", 3, "pcs", 4_500m),
            new RequisitionLineInput("Docking station", 3, "pcs", 333.333m));

        requisition.EstimatedNetTotal.Should().Be(13_500m + 1_000m);
        requisition.EstimatedTotalWithVat.Should().Be(16_675m);
        requisition.Lines.Select(l => l.LineNumber).Should().Equal(1, 2);
    }

    [Theory]
    [InlineData(0, 10)]
    [InlineData(-1, 10)]
    [InlineData(1.2345, 10)]
    [InlineData(1, -5)]
    [InlineData(1, 1.23456)]
    public void A_line_needs_a_positive_quantity_and_a_valid_price(decimal quantity, decimal price)
    {
        PurchaseRequisition.CreateDraft(Guid.NewGuid(), "PR-1", Guid.NewGuid(), Guid.NewGuid(),
                Details(new RequisitionLineInput("Laptop", quantity, "pcs", price)), Today)
            .Error!.Code.Should().Be("requisition.line_invalid");
    }

    [Fact]
    public void Needed_by_must_be_in_the_future()
    {
        var details = Details() with { NeededBy = Today };

        PurchaseRequisition.CreateDraft(Guid.NewGuid(), "PR-1", Guid.NewGuid(), Guid.NewGuid(), details, Today)
            .Error.Should().Be(RequisitionErrors.NeededByInPast);
    }

    [Fact]
    public void Life_cycle_from_draft_to_ordered()
    {
        var requisition = Draft();
        var approvalId = Guid.NewGuid();

        requisition.Submit(approvalId).IsSuccess.Should().BeTrue();
        requisition.UpdateDraft(requisition.CostCenterId, Details(), Today).Error.Should().Be(RequisitionErrors.NotDraft);
        requisition.MarkApproved().IsSuccess.Should().BeTrue();
        requisition.ChooseSourcingRoute(SourcingRoute.Rfq, ProcurementSettings.Default.DirectPurchaseLimit).IsSuccess.Should().BeTrue();
        requisition.MarkOrdered().IsSuccess.Should().BeTrue();

        requisition.Status.Should().Be(RequisitionStatus.Ordered);
        requisition.ApprovalRequestId.Should().Be(approvalId);
    }

    [Fact]
    public void Direct_purchase_is_allowed_only_up_to_the_limit()
    {
        var small = Draft(new RequisitionLineInput("Toner", 2, "pcs", 400m));
        var large = Draft(new RequisitionLineInput("Server", 1, "pcs", 40_000m));
        foreach (var requisition in new[] { small, large })
        {
            requisition.Submit(Guid.NewGuid());
            requisition.MarkApproved();
        }

        small.ChooseSourcingRoute(SourcingRoute.DirectPurchase, 5_000m).IsSuccess.Should().BeTrue();
        large.ChooseSourcingRoute(SourcingRoute.DirectPurchase, 5_000m).Error!.Code.Should().Be("requisition.above_direct_limit");
    }

    [Fact]
    public void A_rejected_requisition_keeps_the_reason_and_cannot_be_sourced()
    {
        var requisition = Draft();
        requisition.Submit(Guid.NewGuid());

        requisition.MarkRejected("Not in this year's budget").IsSuccess.Should().BeTrue();

        requisition.RejectionReason.Should().Be("Not in this year's budget");
        requisition.ChooseSourcingRoute(SourcingRoute.Rfq, 5_000m).Error!.Code.Should().Be("requisition.invalid_status_change");
    }

    [Fact]
    public void Cancel_is_allowed_until_sourcing_starts()
    {
        var pending = Draft();
        pending.Submit(Guid.NewGuid());
        var sourcing = Draft();
        sourcing.Submit(Guid.NewGuid());
        sourcing.MarkApproved();
        sourcing.ChooseSourcingRoute(SourcingRoute.Rfq, 5_000m);

        pending.Cancel().IsSuccess.Should().BeTrue();
        sourcing.Cancel().Error!.Code.Should().Be("requisition.invalid_status_change");
    }

    [Fact]
    public void Procurement_settings_are_validated()
    {
        var organization = Organization.Create("Acme Trading").Value;

        organization.UpdateSettings(ProcurementSettings.Default with { MinimumSuppliersPerRfq = 0 }).Error.Should().Be(IdentityErrors.SettingsInvalid);
        organization.UpdateSettings(ProcurementSettings.Default with { DirectPurchaseLimit = 10_000m }).IsSuccess.Should().BeTrue();
        organization.Settings.DirectPurchaseLimit.Should().Be(10_000m);
    }
}
