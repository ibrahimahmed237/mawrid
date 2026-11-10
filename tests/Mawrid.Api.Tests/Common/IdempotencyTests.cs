using System.Net;
using System.Net.Http.Json;
using Mawrid.Api.Common.Idempotency;
using Mawrid.Api.Tests.Infrastructure;
using Mawrid.Application.Common.Paging;
using Mawrid.Application.Suppliers;

namespace Mawrid.Api.Tests.Common;

[Collection(ApiCollection.Name)]
public class IdempotencyTests(MawridApiFactory factory)
{
    private static HttpRequestMessage RegisterRequest(string key, string vatNumber, string name = "Idempotent Supplier") =>
        new(HttpMethod.Post, "/api/v1/suppliers")
        {
            Headers = { { IdempotencyMiddleware.HeaderName, key } },
            Content = JsonContent.Create(new
            {
                nameAr = "مورد", nameEn = name, vatNumber, crNumber = "1010123456",
                contact = new { name = "Ali", email = "ali@x.sa" },
            }),
        };

    [Fact]
    public async Task A_retry_with_the_same_key_returns_the_first_response_and_does_the_work_once()
    {
        var flow = await SupplierFlow.StartAsync(factory);
        var key = Guid.NewGuid().ToString();
        var vat = SupplierFlow.NewVatNumber();

        var first = await flow.Buyer.SendAsync(RegisterRequest(key, vat));
        var retry = await flow.Buyer.SendAsync(RegisterRequest(key, vat));

        first.StatusCode.Should().Be(HttpStatusCode.Created);
        retry.StatusCode.Should().Be(HttpStatusCode.Created);
        retry.Headers.GetValues(IdempotencyMiddleware.ReplayedHeaderName).Should().ContainSingle("true");
        retry.Headers.Location.Should().Be(first.Headers.Location);
        (await retry.Content.ReadAsStringAsync()).Should().Be(await first.Content.ReadAsStringAsync());

        var list = await (await flow.Buyer.GetAsync($"/api/v1/suppliers?search={vat}")).ShouldBeOkAsync<PagedList<SupplierSummaryDto>>();
        list.Total.Should().Be(1);
    }

    [Fact]
    public async Task The_same_key_with_a_different_body_is_an_error()
    {
        var flow = await SupplierFlow.StartAsync(factory);
        var key = Guid.NewGuid().ToString();

        await flow.Buyer.SendAsync(RegisterRequest(key, SupplierFlow.NewVatNumber()));
        var different = await flow.Buyer.SendAsync(RegisterRequest(key, SupplierFlow.NewVatNumber(), "Other name"));

        await different.ShouldBeProblemAsync(HttpStatusCode.UnprocessableEntity, "idempotency.key_reused");
    }

    [Fact]
    public async Task Keys_are_scoped_to_the_organization()
    {
        var flowA = await SupplierFlow.StartAsync(factory);
        var flowB = await SupplierFlow.StartAsync(factory);
        var key = Guid.NewGuid().ToString();

        var a = await flowA.Buyer.SendAsync(RegisterRequest(key, SupplierFlow.NewVatNumber()));
        var b = await flowB.Buyer.SendAsync(RegisterRequest(key, SupplierFlow.NewVatNumber()));

        a.StatusCode.Should().Be(HttpStatusCode.Created);
        b.StatusCode.Should().Be(HttpStatusCode.Created);
        b.Headers.Contains(IdempotencyMiddleware.ReplayedHeaderName).Should().BeFalse();
    }

    [Fact]
    public async Task An_invalid_key_is_rejected()
    {
        var flow = await SupplierFlow.StartAsync(factory);

        var response = await flow.Buyer.SendAsync(RegisterRequest("bad key with spaces", SupplierFlow.NewVatNumber()));

        await response.ShouldBeProblemAsync(HttpStatusCode.BadRequest, "idempotency.key_invalid");
    }

    [Fact]
    public async Task A_failed_request_is_replayed_too()
    {
        var flow = await SupplierFlow.StartAsync(factory);
        var key = Guid.NewGuid().ToString();

        var first = await flow.Buyer.SendAsync(RegisterRequest(key, "123"));
        var retry = await flow.Buyer.SendAsync(RegisterRequest(key, "123"));

        await first.ShouldBeProblemAsync(HttpStatusCode.BadRequest, "supplier.vat_number_invalid");
        await retry.ShouldBeProblemAsync(HttpStatusCode.BadRequest, "supplier.vat_number_invalid");
        retry.Headers.Contains(IdempotencyMiddleware.ReplayedHeaderName).Should().BeTrue();
    }
}
