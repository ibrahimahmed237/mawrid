using System.Net;
using System.Net.Http.Headers;
using System.Text;
using Mawrid.Api.Tests.Infrastructure;
using Mawrid.Domain.Identity;

namespace Mawrid.Api.Tests.Files;

[Collection(ApiCollection.Name)]
public class FileUploadTests(MawridApiFactory factory)
{
    private async Task<HttpClient> BuyerAsync()
    {
        var organizationId = await factory.CreateOrganizationAsync();
        var email = TestData.UniqueEmail("buyer");
        await factory.CreateUserAsync(organizationId, email, Role.Buyer);
        return await factory.CreateClientForAsync(email);
    }

    private static MultipartFormDataContent Form(byte[] bytes, string contentType, string fileName = "file.bin")
    {
        var form = new MultipartFormDataContent();
        var file = new ByteArrayContent(bytes);
        file.Headers.ContentType = new MediaTypeHeaderValue(contentType);
        form.Add(file, "file", fileName);
        return form;
    }

    [Fact]
    public async Task A_pdf_can_be_uploaded_and_downloaded()
    {
        var client = await BuyerAsync();
        var content = Encoding.ASCII.GetBytes("%PDF-1.7 quotation");

        var uploaded = await SupplierFlow.UploadPdfAsync(client, "%PDF-1.7 quotation");
        var download = await client.GetAsync($"/api/v1/files/{uploaded.Id}");

        uploaded.SizeBytes.Should().Be(content.Length);
        uploaded.Sha256.Should().HaveLength(64);
        download.StatusCode.Should().Be(HttpStatusCode.OK);
        download.Content.Headers.ContentType!.MediaType.Should().Be("application/pdf");
        (await download.Content.ReadAsByteArrayAsync()).Should().Equal(content);
    }

    [Fact]
    public async Task A_disallowed_type_is_rejected()
    {
        var client = await BuyerAsync();

        var response = await client.PostAsync("/api/v1/files", Form([0x4D, 0x5A, 0x90], "application/x-msdownload", "setup.exe"));

        await response.ShouldBeProblemAsync(HttpStatusCode.BadRequest, "file.type_not_allowed");
    }

    [Fact]
    public async Task Content_that_does_not_match_the_type_is_rejected()
    {
        var client = await BuyerAsync();

        var response = await client.PostAsync("/api/v1/files", Form(Encoding.ASCII.GetBytes("MZ not really a pdf"), "application/pdf", "fake.pdf"));

        await response.ShouldBeProblemAsync(HttpStatusCode.BadRequest, "file.content_mismatch");
    }

    [Fact]
    public async Task The_eicar_test_file_is_blocked_by_the_scanner()
    {
        var client = await BuyerAsync();
        var eicar = Encoding.ASCII.GetBytes("%PDF-1.4 X5O!P%@AP[4\\PZX54(P^)7CC)7}$EICAR-STANDARD-ANTIVIRUS-TEST-FILE!$H+H*");

        var response = await client.PostAsync("/api/v1/files", Form(eicar, "application/pdf", "infected.pdf"));

        await response.ShouldBeProblemAsync(HttpStatusCode.UnprocessableEntity, "file.malware_detected");
    }

    [Fact]
    public async Task Files_of_another_organization_are_not_found()
    {
        var owner = await BuyerAsync();
        var stranger = await BuyerAsync();
        var file = await SupplierFlow.UploadPdfAsync(owner);

        var response = await stranger.GetAsync($"/api/v1/files/{file.Id}");

        await response.ShouldBeProblemAsync(HttpStatusCode.NotFound, "file.not_found");
    }
}
