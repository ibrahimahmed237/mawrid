using Mawrid.Domain.Suppliers;

namespace Mawrid.Domain.Tests.Suppliers;

public class SupplierTests
{
    private static readonly DateTimeOffset Now = new(2026, 11, 10, 9, 0, 0, TimeSpan.Zero);
    private static readonly DateOnly Today = DateOnly.FromDateTime(Now.UtcDateTime);
    private static readonly Guid SupplierUser = Guid.NewGuid();
    private static readonly Guid Admin = Guid.NewGuid();
    private const string ValidIban = "SA0380000000608010167519";
    private const string OtherIban = "SA0910000000000000000001";

    private static Supplier NewSupplier() => Supplier.Register(
        Guid.NewGuid(), "شركة الأفق للتوريدات", "Al Ufuq Supplies", "300000000000003", "1010123456",
        new SupplierContact("Omar", "omar@ufuq.sa", null)).Value;

    private static Supplier ReadyForReview()
    {
        var supplier = NewSupplier();
        foreach (var type in Supplier.RequiredDocuments)
        {
            supplier.AddDocument(type, Guid.NewGuid(), Today.AddYears(1), Today, Now).IsSuccess.Should().BeTrue();
        }

        supplier.RequestBankAccount(ValidIban, "Al Rajhi Bank", "Al Ufuq Supplies", SupplierUser, Now).IsSuccess.Should().BeTrue();
        return supplier;
    }

    private static Supplier Approved()
    {
        var supplier = ReadyForReview();
        supplier.SubmitForReview(Today).IsSuccess.Should().BeTrue();
        supplier.Approve(Admin, Today, Now).IsSuccess.Should().BeTrue();
        return supplier;
    }

    [Fact]
    public void Register_starts_in_registered_and_raises_an_event()
    {
        var supplier = NewSupplier();

        supplier.Status.Should().Be(SupplierStatus.Registered);
        supplier.VatNumber.Value.Should().Be("300000000000003");
        supplier.DomainEvents.Should().ContainSingle().Which.Should().BeOfType<SupplierRegistered>();
    }

    [Fact]
    public void Register_rejects_a_bad_vat_number()
    {
        Supplier.Register(Guid.NewGuid(), "اسم", "Name", "123", "1010123456", new SupplierContact("O", "o@x.sa", null))
            .Error.Should().Be(SupplierErrors.VatNumberInvalid);
    }

    [Fact]
    public void Submit_needs_all_required_documents_and_a_bank_account()
    {
        var supplier = NewSupplier();

        supplier.SubmitForReview(Today).Error.Should().Be(SupplierErrors.RequiredDocumentsMissing);

        foreach (var type in Supplier.RequiredDocuments)
        {
            supplier.AddDocument(type, Guid.NewGuid(), Today.AddMonths(6), Today, Now);
        }

        supplier.SubmitForReview(Today).Error.Should().Be(SupplierErrors.BankAccountMissing);
    }

    [Fact]
    public void An_expired_document_does_not_count()
    {
        var supplier = ReadyForReview();

        supplier.SubmitForReview(Today.AddYears(2)).Error.Should().Be(SupplierErrors.RequiredDocumentsMissing);
    }

    [Fact]
    public void A_document_that_already_expired_cannot_be_added()
    {
        NewSupplier().AddDocument(SupplierDocumentType.CrCertificate, Guid.NewGuid(), Today, Today, Now)
            .Error.Should().Be(SupplierErrors.DocumentAlreadyExpired);
    }

    [Fact]
    public void The_newest_document_of_a_type_replaces_the_older_one()
    {
        var supplier = ReadyForReview();
        var later = Now.AddDays(1);

        supplier.AddDocument(SupplierDocumentType.CrCertificate, Guid.NewGuid(), Today.AddDays(10), Today, later);

        supplier.CurrentDocuments.Single(d => d.Type == SupplierDocumentType.CrCertificate).ExpiresOn.Should().Be(Today.AddDays(10));
        supplier.HasValidRequiredDocuments(Today.AddDays(11)).Should().BeFalse();
    }

    [Fact]
    public void Approve_verifies_the_first_bank_account_and_makes_the_supplier_awardable()
    {
        var supplier = Approved();

        supplier.Status.Should().Be(SupplierStatus.Approved);
        supplier.ActiveBankAccount!.Iban.Value.Should().Be(ValidIban);
        supplier.ActiveBankAccount.VerifiedBy.Should().Be(Admin);
        supplier.HasPendingBankChange.Should().BeFalse();
        supplier.IsAwardable(Today).Should().BeTrue();
        supplier.IsAwardable(Today.AddYears(2)).Should().BeFalse("its documents will have expired");
    }

    [Fact]
    public void Approve_fails_when_the_approver_added_the_bank_account()
    {
        var supplier = NewSupplier();
        foreach (var type in Supplier.RequiredDocuments)
        {
            supplier.AddDocument(type, Guid.NewGuid(), Today.AddYears(1), Today, Now);
        }

        supplier.RequestBankAccount(ValidIban, "Al Rajhi Bank", "Al Ufuq", Admin, Now);
        supplier.SubmitForReview(Today);

        supplier.Approve(Admin, Today, Now).Error.Should().Be(SupplierErrors.FourEyesRequired);
        supplier.Status.Should().Be(SupplierStatus.UnderReview);
    }

    [Fact]
    public void A_bank_change_after_approval_needs_a_second_person()
    {
        var supplier = Approved();

        supplier.RequestBankAccount(OtherIban, "SNB", "Al Ufuq Supplies", SupplierUser, Now.AddDays(1)).IsSuccess.Should().BeTrue();
        var pending = supplier.BankAccounts.Single(a => a.Status == BankAccountStatus.PendingVerification);

        supplier.HasPendingBankChange.Should().BeTrue();
        supplier.ActiveBankAccount!.Iban.Value.Should().Be(ValidIban, "payments keep going to the old account until the change is verified");
        supplier.VerifyBankAccount(pending.Id, SupplierUser, Now.AddDays(1)).Error.Should().Be(SupplierErrors.FourEyesRequired);

        supplier.VerifyBankAccount(pending.Id, Admin, Now.AddDays(2)).IsSuccess.Should().BeTrue();
        supplier.ActiveBankAccount!.Iban.Value.Should().Be(OtherIban);
        supplier.BankAccounts.Should().ContainSingle(a => a.Status == BankAccountStatus.Superseded);
    }

    [Fact]
    public void Only_one_bank_change_can_wait_at_a_time()
    {
        var supplier = Approved();
        supplier.RequestBankAccount(OtherIban, "SNB", "Al Ufuq", SupplierUser, Now);

        supplier.RequestBankAccount(ValidIban, "SNB", "Al Ufuq", SupplierUser, Now).Error.Should().Be(SupplierErrors.BankChangeAlreadyPending);
    }

    [Fact]
    public void Block_and_unblock_need_the_right_status_and_a_reason()
    {
        var supplier = Approved();

        supplier.Block("").Error.Should().Be(SupplierErrors.ReasonRequired);
        supplier.Block("Late deliveries").IsSuccess.Should().BeTrue();
        supplier.IsAwardable(Today).Should().BeFalse();
        supplier.StatusReason.Should().Be("Late deliveries");

        supplier.Unblock().IsSuccess.Should().BeTrue();
        supplier.Status.Should().Be(SupplierStatus.Approved);
        supplier.StatusReason.Should().BeNull();
    }

    [Fact]
    public void Status_changes_follow_the_life_cycle()
    {
        var supplier = NewSupplier();

        supplier.Approve(Admin, Today, Now).Error!.Code.Should().Be("supplier.invalid_status_change");
        supplier.Block("reason").Error!.Code.Should().Be("supplier.invalid_status_change");
        supplier.Reject("reason").Error!.Code.Should().Be("supplier.invalid_status_change");
    }

    [Fact]
    public void A_rejected_supplier_cannot_be_changed()
    {
        var supplier = ReadyForReview();
        supplier.SubmitForReview(Today);
        supplier.Reject("Missing ISO certificate").IsSuccess.Should().BeTrue();

        supplier.AddDocument(SupplierDocumentType.Other, Guid.NewGuid(), Today.AddYears(1), Today, Now).Error.Should().Be(SupplierErrors.SupplierRejected);
        supplier.RequestBankAccount(OtherIban, "SNB", "X Co", SupplierUser, Now).Error.Should().Be(SupplierErrors.SupplierRejected);
    }
}
