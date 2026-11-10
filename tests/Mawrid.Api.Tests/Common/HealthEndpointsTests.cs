using System.Net;
using System.Net.Http.Json;
using Mawrid.Api.Tests.Infrastructure;
using Microsoft.Extensions.DependencyInjection;

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

    [Fact]
    public async Task Ready_reports_degraded_when_the_outbox_is_late()
    {
        var late = new Mawrid.Infrastructure.Outbox.OutboxMessage
        {
            Id = Guid.NewGuid(),
            Type = "Test.Late",
            Content = "{}",
            OccurredOnUtc = DateTimeOffset.UtcNow.AddMinutes(-10),
            NextAttemptAtUtc = DateTimeOffset.UtcNow.AddMinutes(-10),
        };
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<Mawrid.Infrastructure.Persistence.AppDbContext>();
        db.OutboxMessages.Add(late);
        await db.SaveChangesAsync();
        try
        {
            var response = await factory.CreateClient().GetAsync("/health/ready");

            response.StatusCode.Should().Be(HttpStatusCode.OK, "a late outbox does not stop the app from serving requests");
            (await response.Content.ReadFromJsonAsync<HealthBody>())!.Status.Should().Be("Degraded");
        }
        finally
        {
            db.OutboxMessages.Remove(late);
            await db.SaveChangesAsync();
        }
    }
}
