using System.Net;
using System.Net.Http.Json;
using Mawrid.Api.Tests.Infrastructure;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;

namespace Mawrid.Api.Tests.Common;

[Collection(ApiCollection.Name)]
public sealed class HardeningTests(MawridApiFactory factory)
{
    [Fact]
    public async Task Too_many_logins_from_one_address_get_429_with_retry_after()
    {
        await using var strict = factory.WithWebHostBuilder(b => b.UseSetting("RateLimits:LoginPerMinute", "3"));
        var client = strict.CreateClient();

        for (var i = 0; i < 3; i++)
        {
            (await client.PostAsJsonAsync("/api/v1/auth/login", new { email = "nobody@test.sa", password = "wrong-password" }))
                .StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        }

        var blocked = await client.PostAsJsonAsync("/api/v1/auth/login", new { email = "nobody@test.sa", password = "wrong-password" });

        await blocked.ShouldBeProblemAsync(HttpStatusCode.TooManyRequests, "request.rate_limited");
        blocked.Headers.RetryAfter.Should().NotBeNull();
    }

    [Fact]
    public async Task The_supplier_portal_is_limited_per_user()
    {
        var suppliers = await SupplierFlow.StartAsync(factory);
        var supplierId = await suppliers.RegisterAsync();
        await using var strict = factory.WithWebHostBuilder(b => b.UseSetting("RateLimits:SupplierPerMinute", "2"));
        var email = TestData.UniqueEmail("limited");
        await factory.CreateUserAsync(suppliers.OrganizationId, email, supplierId, [new Mawrid.Domain.Common.RoleAssignment(Mawrid.Domain.Common.Role.Supplier)]);
        var client = strict.CreateClient();
        var token = await client.LoginAsync(email);
        client.DefaultRequestHeaders.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", token.AccessToken);

        (await client.GetAsync("/api/v1/supplier/rfqs")).StatusCode.Should().Be(HttpStatusCode.OK);
        (await client.GetAsync("/api/v1/supplier/rfqs")).StatusCode.Should().Be(HttpStatusCode.OK);
        (await client.GetAsync("/api/v1/supplier/rfqs")).StatusCode.Should().Be(HttpStatusCode.TooManyRequests);
    }

    [Theory]
    [InlineData("/health/live")]
    [InlineData("/api/v1/me")]
    public async Task Responses_carry_security_headers(string path)
    {
        var response = await factory.CreateClient().GetAsync(path);

        response.Headers.GetValues("X-Content-Type-Options").Should().Equal("nosniff");
        response.Headers.GetValues("X-Frame-Options").Should().Equal("DENY");
        response.Headers.GetValues("Referrer-Policy").Should().Equal("no-referrer");
        response.Headers.CacheControl!.NoStore.Should().BeTrue();
    }
}
