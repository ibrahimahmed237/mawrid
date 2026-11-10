using System.Net.Http.Json;
using Mawrid.Api.Tests.Infrastructure;
using Mawrid.Application.Common.Abstractions;
using Mawrid.Domain.Identity;
using Mawrid.Infrastructure.Identity;
using Mawrid.Infrastructure.Outbox;
using Mawrid.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Mawrid.Api.Tests.Identity;

/// <summary>ADR-0004: audit rows and outbox messages are written with the change, and the outbox delivers with retries.</summary>
[Collection(ApiCollection.Name)]
public class AuditAndOutboxTests(MawridApiFactory factory)
{
    private async Task<(Guid OrganizationId, Guid AdminId, Guid NewUserId, string CorrelationId)> CreateUserThroughApiAsync()
    {
        var organizationId = await factory.CreateOrganizationAsync();
        var adminEmail = TestData.UniqueEmail("admin");
        var adminId = await factory.CreateUserAsync(organizationId, adminEmail, Role.Admin);
        var admin = await factory.CreateClientForAsync(adminEmail);
        var correlationId = Guid.NewGuid().ToString("N");

        using var request = new HttpRequestMessage(HttpMethod.Post, "/api/v1/users")
        {
            Content = JsonContent.Create(new
            {
                email = TestData.UniqueEmail("new"),
                displayName = "Audited Person",
                password = TestData.Password,
                roles = new[] { new { role = "Buyer" } },
            }),
        };
        request.Headers.Add("X-Correlation-Id", correlationId);
        var response = await admin.SendAsync(request);
        var created = await response.ShouldBeOkAsync<Common.CreatedIdBody>(System.Net.HttpStatusCode.Created);
        return (organizationId, adminId, created.Id, correlationId);
    }

    [Fact]
    public async Task Creating_a_user_writes_audit_rows_with_who_and_correlation_id()
    {
        var (organizationId, adminId, newUserId, correlationId) = await CreateUserThroughApiAsync();

        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var entries = await db.AuditEntries.Where(a => a.EntityId == newUserId.ToString()).ToListAsync();

        entries.Should().Contain(a => a.EntityType == "User" && a.Action == "Added");
        entries.Should().Contain(a => a.EntityType == "User.RoleAssignment" && a.Action == "Added" && a.Changes.Contains("Buyer"));
        entries.Should().OnlyContain(a => a.UserId == adminId && a.OrganizationId == organizationId && a.CorrelationId == correlationId);
        entries.Should().NotContain(a => a.Changes.Contains("Hash"));
    }

    [Fact]
    public async Task Domain_events_are_stored_and_delivered_by_the_outbox()
    {
        var (_, _, newUserId, correlationId) = await CreateUserThroughApiAsync();
        var recorder = factory.Services.GetRequiredService<EventRecorder>();
        var processor = factory.Services.GetRequiredService<OutboxProcessor>();

        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var message = await db.OutboxMessages.SingleAsync(m => m.Content.Contains(newUserId.ToString()));
            message.Type.Should().Be(typeof(UserCreated).FullName);
            message.CorrelationId.Should().Be(correlationId);
            message.ProcessedOnUtc.Should().BeNull();
        }

        while (await processor.ProcessBatchAsync(CancellationToken.None) > 0)
        {
        }

        recorder.Events.OfType<UserCreated>().Should().Contain(e => e.UserId == newUserId);
        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var message = await db.OutboxMessages.SingleAsync(m => m.Content.Contains(newUserId.ToString()));
            message.ProcessedOnUtc.Should().NotBeNull();
            message.Attempts.Should().Be(1);
        }
    }

    [Fact]
    public async Task A_failing_handler_schedules_a_retry_with_backoff()
    {
        var recorder = factory.Services.GetRequiredService<EventRecorder>();
        var processor = factory.Services.GetRequiredService<OutboxProcessor>();
        var clock = factory.Services.GetRequiredService<IClock>();
        while (await processor.ProcessBatchAsync(CancellationToken.None) > 0)
        {
        }

        var organizationId = Guid.Empty;
        recorder.FailOn = typeof(OrganizationCreated);
        try
        {
            organizationId = await factory.CreateOrganizationAsync("Retry Org");
            await processor.ProcessBatchAsync(CancellationToken.None);
        }
        finally
        {
            recorder.FailOn = null;
        }

        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var message = await db.OutboxMessages.SingleAsync(m => m.Content.Contains(organizationId.ToString()) && m.Type.EndsWith("OrganizationCreated"));
        message.ProcessedOnUtc.Should().BeNull();
        message.Attempts.Should().Be(1);
        message.LastError.Should().Contain("Simulated failure");
        message.NextAttemptAtUtc.Should().BeAfter(clock.UtcNow);
        message.LockedUntilUtc.Should().BeNull();
    }

    [Theory]
    [InlineData(1, 2)]
    [InlineData(3, 8)]
    [InlineData(20, 1800)]
    public void Backoff_doubles_and_is_capped_at_30_minutes(int attempts, int expectedSeconds)
    {
        OutboxProcessor.Backoff(attempts).Should().Be(TimeSpan.FromSeconds(expectedSeconds));
    }
}
