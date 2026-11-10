using System.Net;
using System.Net.Http.Json;
using Mawrid.Api.Tests.Infrastructure;

namespace Mawrid.Api.Tests.Common;

[Collection(ApiCollection.Name)]
public class HealthEndpointsTests(MawridApiFactory factory)
{
    private sealed record HealthBody(string Status);

    [Fact]
    public async Task Live_returns_healthy()
    {
        var response = await factory.CreateClient().GetAsync("/health/live");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        (await response.Content.ReadFromJsonAsync<HealthBody>())!.Status.Should().Be("Healthy");
    }

    [Fact]
    public async Task Ready_returns_healthy_when_the_database_answers()
    {
        var response = await factory.CreateClient().GetAsync("/health/ready");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await response.Content.ReadAsStringAsync();
        body.Should().Contain("\"sql-server\"").And.Contain("Healthy");
    }
}
