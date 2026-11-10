using System.Net;
using System.Net.Http.Json;
using Mawrid.Api.Tests.Infrastructure;
using Mawrid.Application.Common.Paging;
using Mawrid.Application.Suppliers;

namespace Mawrid.Api.Tests.Suppliers;

[Collection(ApiCollection.Name)]
public class SupplierOnboardingTests(MawridApiFactory factory)
{
    [Fact]
    public async Task Full_onboarding_from_registration_to_approval()
    {
        var flow = await SupplierFlow.StartAsync(factory);

        var (supplierId, supplierUser) = await flow.ApprovedSupplierAsync();

        var supplier = await (await flow.Buyer.GetAsync($"/api/v1/suppliers/{supplierId}")).ShouldBeOkAsync<SupplierDto>();
        supplier.Status.Should().Be("Approved");
        supplier.IsAwardable.Should().BeTrue();
        supplier.Documents.Should().HaveCount(3).And.OnlyContain(d => d.IsValid);
        supplier.BankAccounts.Should().ContainSingle().Which.Should().Match<BankAccountDto>(a =>
            a.Status == "Verified" && a.MaskedIban == "SA** **** **** **** **** 7519");

        var profile = await (await supplierUser.GetAsync("/api/v1/supplier/profile")).ShouldBeOkAsync<SupplierDto>();
        profile.Id.Should().Be(supplierId);
    }

    [Fact]
    public async Task Vat_number_is_unique_within_the_organization()
    {
        var flow = await SupplierFlow.StartAsync(factory);
        var vat = SupplierFlow.NewVatNumber();
        await flow.RegisterAsync(vat);

        var duplicate = await flow.Buyer.PostAsJsonAsync("/api/v1/suppliers", new
        {
            nameAr = "مورد آخر", nameEn = "Another Supplier", vatNumber = vat, crNumber = "1010999999",
            contact = new { name = "Ali", email = "ali@x.sa" },
        });

        await duplicate.ShouldBeProblemAsync(HttpStatusCode.Conflict, "supplier.vat_number_taken");
    }

    [Fact]
    public async Task Invalid_vat_number_is_rejected()
    {
        var flow = await SupplierFlow.StartAsync(factory);

        var response = await flow.Buyer.PostAsJsonAsync("/api/v1/suppliers", new
        {
            nameAr = "مورد", nameEn = "Supplier", vatNumber = "123456789012345", crNumber = "1010999999",
            contact = new { name = "Ali", email = "ali@x.sa" },
        });

        await response.ShouldBeProblemAsync(HttpStatusCode.BadRequest, "supplier.vat_number_invalid");
    }

    [Fact]
    public async Task Submitting_without_documents_is_refused()
    {
        var flow = await SupplierFlow.StartAsync(factory);
        var supplierId = await flow.RegisterAsync();
        var supplierUser = await flow.InviteSupplierUserAsync(supplierId);

        var submit = await supplierUser.PostAsync("/api/v1/supplier/submit", null);

        await submit.ShouldBeProblemAsync(HttpStatusCode.UnprocessableEntity, "supplier.documents_missing");
    }

    [Fact]
    public async Task A_supplier_user_cannot_attach_another_companys_file()
    {
        var flow = await SupplierFlow.StartAsync(factory);
        var first = await flow.InviteSupplierUserAsync(await flow.RegisterAsync());
        var second = await flow.InviteSupplierUserAsync(await flow.RegisterAsync());
        var fileOfFirst = await SupplierFlow.UploadPdfAsync(first);

        var attach = await second.PostAsJsonAsync("/api/v1/supplier/documents",
            new { type = "CrCertificate", fileId = fileOfFirst.Id, expiresOn = DateOnly.FromDateTime(DateTime.UtcNow.AddYears(1)) });
        var download = await second.GetAsync($"/api/v1/files/{fileOfFirst.Id}");

        await attach.ShouldBeProblemAsync(HttpStatusCode.NotFound, "supplier.file_not_found");
        await download.ShouldBeProblemAsync(HttpStatusCode.NotFound, "file.not_found");
    }

    [Fact]
    public async Task Bank_change_after_approval_waits_for_a_second_person()
    {
        var flow = await SupplierFlow.StartAsync(factory);
        var (supplierId, supplierUser) = await flow.ApprovedSupplierAsync();

        var change = await supplierUser.PostAsJsonAsync("/api/v1/supplier/bank-accounts",
            new { iban = SupplierFlow.OtherIban, bankName = "Saudi National Bank", accountName = "Al Ufuq Supplies" });
        var pending = await (await flow.Admin.GetAsync($"/api/v1/suppliers/{supplierId}")).ShouldBeOkAsync<SupplierDto>();
        var newAccount = pending.BankAccounts.Single(a => a.Status == "PendingVerification");
        var verify = await flow.Admin.PostAsync($"/api/v1/suppliers/{supplierId}/bank-accounts/{newAccount.Id}/verify", null);
        var after = await (await flow.Admin.GetAsync($"/api/v1/suppliers/{supplierId}")).ShouldBeOkAsync<SupplierDto>();

        change.StatusCode.Should().Be(HttpStatusCode.NoContent);
        pending.HasPendingBankChange.Should().BeTrue();
        verify.StatusCode.Should().Be(HttpStatusCode.NoContent);
        after.HasPendingBankChange.Should().BeFalse();
        after.BankAccounts.Should().Contain(a => a.Status == "Verified" && a.MaskedIban.EndsWith("0001"));
        after.BankAccounts.Should().Contain(a => a.Status == "Superseded");
    }

    [Fact]
    public async Task Block_with_a_reason_makes_the_supplier_not_awardable()
    {
        var flow = await SupplierFlow.StartAsync(factory);
        var (supplierId, _) = await flow.ApprovedSupplierAsync();

        var withoutReason = await flow.Admin.PostAsJsonAsync($"/api/v1/suppliers/{supplierId}/block", new { reason = "" });
        var block = await flow.Admin.PostAsJsonAsync($"/api/v1/suppliers/{supplierId}/block", new { reason = "Repeated late deliveries" });
        var blocked = await (await flow.Buyer.GetAsync("/api/v1/suppliers?status=Blocked")).ShouldBeOkAsync<PagedList<SupplierSummaryDto>>();

        await withoutReason.ShouldBeProblemAsync(HttpStatusCode.BadRequest, "request.validation_failed");
        block.StatusCode.Should().Be(HttpStatusCode.NoContent);
        blocked.Items.Should().ContainSingle(s => s.Id == supplierId).Which.IsAwardable.Should().BeFalse();
    }

    [Fact]
    public async Task Only_admins_approve_and_only_procurement_registers()
    {
        var flow = await SupplierFlow.StartAsync(factory);
        var supplierId = await flow.RegisterAsync();
        var supplierUser = await flow.InviteSupplierUserAsync(supplierId);

        var buyerApproves = await flow.Buyer.PostAsync($"/api/v1/suppliers/{supplierId}/approve", null);
        var supplierLists = await supplierUser.GetAsync("/api/v1/suppliers");

        await buyerApproves.ShouldBeProblemAsync(HttpStatusCode.Forbidden, "auth.forbidden");
        await supplierLists.ShouldBeProblemAsync(HttpStatusCode.Forbidden, "auth.forbidden");
    }

    [Fact]
    public async Task Search_finds_suppliers_by_name_or_vat_number()
    {
        var flow = await SupplierFlow.StartAsync(factory);
        var vat = SupplierFlow.NewVatNumber();
        var supplierId = await flow.RegisterAsync(vat);

        var byVat = await (await flow.Buyer.GetAsync($"/api/v1/suppliers?search={vat[3..10]}")).ShouldBeOkAsync<PagedList<SupplierSummaryDto>>();

        byVat.Items.Should().ContainSingle(s => s.Id == supplierId);
    }

    [Fact]
    public async Task Supplier_user_for_an_unknown_supplier_cannot_be_created()
    {
        var flow = await SupplierFlow.StartAsync(factory);

        var response = await flow.Admin.PostAsJsonAsync("/api/v1/users", new
        {
            email = TestData.UniqueEmail(), displayName = "Ghost", password = TestData.Password,
            roles = new[] { new { role = "Supplier" } }, supplierId = Guid.NewGuid(),
        });

        await response.ShouldBeProblemAsync(HttpStatusCode.NotFound, "user.supplier_not_found");
    }
}
