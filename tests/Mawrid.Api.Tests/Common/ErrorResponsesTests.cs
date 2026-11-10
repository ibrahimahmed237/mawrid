using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Mawrid.Api.Common.Correlation;
using Mawrid.Api.Tests.Infrastructure;

namespace Mawrid.Api.Tests.Common;

[Collection(ApiCollection.Name)]
public class ErrorResponsesTests(MawridApiFactory factory)
{
    [Fact]
    public async Task Anonymous_request_to_an_unknown_route_gets_401_without_revealing_routes()
    {
        var response = await factory.CreateClient().GetAsync("/does-not-exist");

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        response.Content.Headers.ContentType!.MediaType.Should().Be("application/problem+json");
        var problem = await ReadJson(response);
        problem.GetProperty("code").GetString().Should().Be("auth.unauthenticated");
        problem.GetProperty("traceId").GetString().Should().NotBeNullOrEmpty();
    }

    [Fact]
    public async Task Authenticated_request_to_an_unknown_route_gets_404()
    {
        var organizationId = await factory.CreateOrganizationAsync();
        var email = TestData.UniqueEmail();
        await factory.CreateUserAsync(organizationId, email, Domain.Identity.Role.Buyer);
        var client = await factory.CreateClientForAsync(email);

        var response = await client.GetAsync("/does-not-exist");

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await ReadJson(response)).GetProperty("code").GetString().Should().Be("request.not_found");
    }

    [Fact]
    public async Task Unhandled_exception_returns_500_without_leaking_details()
    {
        var response = await factory.CreateClient().GetAsync("/_test/errors/unhandled");

        response.StatusCode.Should().Be(HttpStatusCode.InternalServerError);
        var body = await response.Content.ReadAsStringAsync();
        body.Should().NotContain("Secret internal detail");
        var problem = JsonDocument.Parse(body).RootElement;
        problem.GetProperty("code").GetString().Should().Be("server.unexpected_error");
    }

    [Fact]
    public async Task Validation_exception_returns_400_with_field_errors()
    {
        var response = await factory.CreateClient().GetAsync("/_test/errors/validation");

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        var problem = await ReadJson(response);
        problem.GetProperty("code").GetString().Should().Be("request.validation_failed");
        problem.GetProperty("errors").GetProperty("Name")[0].GetString().Should().Contain("must not be empty");
    }

    [Fact]
    public async Task Failed_result_returns_its_status_and_code()
    {
        var response = await factory.CreateClient().GetAsync("/_test/errors/conflict");

        response.StatusCode.Should().Be(HttpStatusCode.Conflict);
        var problem = await ReadJson(response);
        problem.GetProperty("code").GetString().Should().Be("supplier.duplicate_vat");
        problem.GetProperty("traceId").GetString().Should().NotBeNullOrEmpty();
    }

    [Fact]
    public async Task Correlation_id_from_the_caller_is_returned_and_used_as_trace_id()
    {
        var client = factory.CreateClient();
        using var request = new HttpRequestMessage(HttpMethod.Get, "/does-not-exist");
        request.Headers.Add(CorrelationIdMiddleware.HeaderName, "abc-123");

        var response = await client.SendAsync(request);

        response.Headers.GetValues(CorrelationIdMiddleware.HeaderName).Should().ContainSingle("abc-123");
        (await ReadJson(response)).GetProperty("traceId").GetString().Should().Be("abc-123");
    }

    [Theory]
    [InlineData("has spaces and <html>")]
    [InlineData("0123456789012345678901234567890123456789012345678901234567890123456789")]
    public async Task Unsafe_or_too_long_correlation_id_is_replaced(string incoming)
    {
        var client = factory.CreateClient();
        using var request = new HttpRequestMessage(HttpMethod.Get, "/health/live");
        request.Headers.TryAddWithoutValidation(CorrelationIdMiddleware.HeaderName, incoming);

        var response = await client.SendAsync(request);

        var returned = response.Headers.GetValues(CorrelationIdMiddleware.HeaderName).Single();
        returned.Should().MatchRegex("^[0-9a-f]{32}$");
    }

    private static async Task<JsonElement> ReadJson(HttpResponseMessage response) =>
        (await response.Content.ReadFromJsonAsync<JsonElement>());
}
