using Mawrid.Domain.Common;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using Mawrid.Application.Files;
using Mawrid.Domain.Identity;

namespace Mawrid.Api.Tests.Infrastructure;

/// <summary>Drives a supplier through onboarding with real HTTP calls.</summary>
public sealed class SupplierFlow
{
    public const string Iban = "SA0380000000608010167519";
    public const string OtherIban = "SA0910000000000000000001";

    private static int _vatCounter = 10000;

    private SupplierFlow(MawridApiFactory factory, Guid organizationId, HttpClient admin, HttpClient buyer)
    {
        Factory = factory;
        OrganizationId = organizationId;
        Admin = admin;
        Buyer = buyer;
    }

    public MawridApiFactory Factory { get; }

    public Guid OrganizationId { get; }

    public HttpClient Admin { get; }

    public HttpClient Buyer { get; }

    public static async Task<SupplierFlow> StartAsync(MawridApiFactory factory)
    {
        var organizationId = await factory.CreateOrganizationAsync();
        var adminEmail = TestData.UniqueEmail("admin");
        var buyerEmail = TestData.UniqueEmail("buyer");
        await factory.CreateUserAsync(organizationId, adminEmail, Role.Admin);
        await factory.CreateUserAsync(organizationId, buyerEmail, Role.Buyer);
        return new SupplierFlow(factory, organizationId, await factory.CreateClientForAsync(adminEmail), await factory.CreateClientForAsync(buyerEmail));
    }

    /// <summary>A unique, valid VAT number: 15 digits starting and ending with 3.</summary>
    public static string NewVatNumber() => $"3{Interlocked.Increment(ref _vatCounter):D13}3";

    public async Task<Guid> RegisterAsync(string? vatNumber = null)
    {
        var response = await Buyer.PostAsJsonAsync("/api/v1/suppliers", new
        {
            nameAr = "شركة الأفق للتوريدات",
            nameEn = $"Al Ufuq Supplies {Guid.NewGuid():N}"[..30],
            vatNumber = vatNumber ?? NewVatNumber(),
            crNumber = "1010123456",
            contact = new { name = "Omar", email = "omar@ufuq.sa", phone = "+966500000000" },
        });
        return (await response.ShouldBeOkAsync<Common.CreatedIdBody>(HttpStatusCode.Created)).Id;
    }

    /// <summary>Creates a supplier user for the supplier (as an admin would invite one) and logs in.</summary>
    public async Task<HttpClient> InviteSupplierUserAsync(Guid supplierId)
    {
        var email = TestData.UniqueEmail("supplier");
        var response = await Admin.PostAsJsonAsync("/api/v1/users", new
        {
            email,
            displayName = "Supplier Contact",
            password = TestData.Password,
            roles = new[] { new { role = "Supplier" } },
            supplierId,
        });
        response.StatusCode.Should().Be(HttpStatusCode.Created, await response.Content.ReadAsStringAsync());
        return await Factory.CreateClientForAsync(email);
    }

    public static async Task<FileDto> UploadPdfAsync(HttpClient client, string content = "%PDF-1.7 test document")
    {
        using var form = new MultipartFormDataContent();
        var file = new ByteArrayContent(System.Text.Encoding.ASCII.GetBytes(content));
        file.Headers.ContentType = new MediaTypeHeaderValue("application/pdf");
        form.Add(file, "file", "document.pdf");
        var response = await client.PostAsync("/api/v1/files", form);
        return await response.ShouldBeOkAsync<FileDto>(HttpStatusCode.Created);
    }

    /// <summary>Uploads the 3 required documents and a bank account, then submits for review.</summary>
    public async Task<HttpClient> OnboardToReviewAsync(Guid supplierId)
    {
        var supplierUser = await InviteSupplierUserAsync(supplierId);
        foreach (var type in new[] { "CrCertificate", "VatCertificate", "BankLetter" })
        {
            var file = await UploadPdfAsync(supplierUser);
            var added = await supplierUser.PostAsJsonAsync("/api/v1/supplier/documents",
                new { type, fileId = file.Id, expiresOn = DateOnly.FromDateTime(DateTime.UtcNow.AddYears(1)) });
            added.StatusCode.Should().Be(HttpStatusCode.NoContent, await added.Content.ReadAsStringAsync());
        }

        var bank = await supplierUser.PostAsJsonAsync("/api/v1/supplier/bank-accounts",
            new { iban = Iban, bankName = "Al Rajhi Bank", accountName = "Al Ufuq Supplies" });
        bank.StatusCode.Should().Be(HttpStatusCode.NoContent, await bank.Content.ReadAsStringAsync());

        var submit = await supplierUser.PostAsync("/api/v1/supplier/submit", null);
        submit.StatusCode.Should().Be(HttpStatusCode.NoContent, await submit.Content.ReadAsStringAsync());
        return supplierUser;
    }

    /// <summary>A fully approved, awardable supplier, and its logged-in supplier user.</summary>
    public async Task<(Guid SupplierId, HttpClient SupplierUser)> ApprovedSupplierAsync()
    {
        var supplierId = await RegisterAsync();
        var supplierUser = await OnboardToReviewAsync(supplierId);
        var approve = await Admin.PostAsync($"/api/v1/suppliers/{supplierId}/approve", null);
        approve.StatusCode.Should().Be(HttpStatusCode.NoContent, await approve.Content.ReadAsStringAsync());
        return (supplierId, supplierUser);
    }
}
