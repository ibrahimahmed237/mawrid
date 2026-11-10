using System.Net;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;

namespace Mawrid.Api.Tests.Common;

/// <summary>When SQL Server is down, the app is alive but not ready.</summary>
public sealed class UnreachableDatabaseTests : IClassFixture<UnreachableDatabaseTests.Factory>
{
    private readonly Factory _factory;

    public UnreachableDatabaseTests(Factory factory) => _factory = factory;

    public sealed class Factory : WebApplicationFactory<Program>
    {
        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            builder.UseEnvironment("Testing");
            builder.UseSetting(
                "ConnectionStrings:Default",
                "Server=127.0.0.1,1;Database=Mawrid;User Id=sa;Password=x;TrustServerCertificate=True;Connect Timeout=2");
        }
    }

    [Fact]
    public async Task Live_is_healthy_and_ready_is_unhealthy()
    {
        var client = _factory.CreateClient();

        var live = await client.GetAsync("/health/live");
        var ready = await client.GetAsync("/health/ready");

        live.StatusCode.Should().Be(HttpStatusCode.OK);
        ready.StatusCode.Should().Be(HttpStatusCode.ServiceUnavailable);
    }
}
