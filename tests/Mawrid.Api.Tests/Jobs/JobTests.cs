using System.Net;
using System.Net.Http.Json;
using Mawrid.Api.Tests.Infrastructure;
using Mawrid.Application.Approvals;
using Mawrid.Application.Identity.Users;
using Mawrid.Application.Sourcing;
using Mawrid.Application.Suppliers;
using Mawrid.Domain.Common;
using Mawrid.Infrastructure.Identity;
using Mawrid.Infrastructure.Jobs;
using Mawrid.Infrastructure.Outbox;
using Mawrid.Infrastructure.Persistence;
using MediatR;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace Mawrid.Api.Tests.Jobs;

[Collection(ApiCollection.Name)]
public sealed class JobTests(MawridApiFactory factory)
{
    [Fact]
    public async Task Rfqs_past_their_deadline_are_closed_and_the_buyer_is_told()
    {
        var flow = await SourcingFlow.StartAsync(factory);
        var rfqId = await flow.PublishedRfqAsync();

        (await factory.RunAsSystemAsync(flow.Approvals.OrganizationId, new CloseDueRfqsCommand())).Value.Should().Be(0);

        using var travel = factory.Clock.Travel(TimeSpan.FromDays(5));
        (await factory.RunAsSystemAsync(flow.Approvals.OrganizationId, new CloseDueRfqsCommand())).Value.Should().Be(1);
        await factory.DeliverOutboxAsync();

        var rfq = await (await flow.Buyer.GetAsync($"/api/v1/rfqs/{rfqId}")).ShouldBeOkAsync<RfqDto>();
        rfq.Status.Should().Be("Closed");
        factory.Emails.To(await EmailOfAsync(flow.Buyer)).Should().Contain(e => e.Subject == $"RFQ {rfq.Number} closed");
    }

    [Fact]
    public async Task Expiring_supplier_documents_are_reported_to_the_supplier_and_the_buyers()
    {
        var suppliers = await SupplierFlow.StartAsync(factory);
        var (_, supplierUser) = await suppliers.ApprovedSupplierAsync();

        // Documents expire one year from now; go to 30 days before that.
        var expiry = DateOnly.FromDateTime(DateTime.UtcNow.AddYears(1));
        using var travel = factory.Clock.Travel(TimeSpan.FromDays(expiry.DayNumber - 30 - factory.Clock.Today.DayNumber));

        var warnings = await factory.RunAsSystemAsync(suppliers.OrganizationId, new WarnExpiringSupplierDocumentsCommand());
        await factory.DeliverOutboxAsync();

        warnings.Value.Should().Be(Mawrid.Domain.Suppliers.Supplier.RequiredDocuments.Count);
        factory.Emails.To(await EmailOfAsync(supplierUser)).Should().Contain(e => e.Subject.EndsWith("expires in 30 days"));
        factory.Emails.To(await EmailOfAsync(suppliers.Buyer)).Should().Contain(e => e.Subject.EndsWith("expires in 30 days"));
    }

    [Fact]
    public async Task Approvers_are_reminded_about_steps_waiting_a_day_or_more()
    {
        var flow = await ApprovalFlow.StartAsync(factory);
        var requisition = await flow.SubmittedRequisitionAsync(2_000m);

        (await factory.RunAsSystemAsync(flow.OrganizationId, new SendApprovalRemindersCommand())).Value.Should().Be(0);

        using var travel = factory.Clock.Travel(TimeSpan.FromDays(1.5));
        (await factory.RunAsSystemAsync(flow.OrganizationId, new SendApprovalRemindersCommand())).Value.Should().Be(1);
        factory.Emails.To(await EmailOfAsync(flow.Manager))
            .Should().Contain(e => e.Subject.StartsWith("Reminder") && e.Subject.Contains(requisition.Number));
    }

    [Fact]
    public async Task A_person_cannot_run_a_system_command()
    {
        var organizationId = await factory.CreateOrganizationAsync();
        await using var scope = factory.Services.CreateAsyncScope();
        scope.ServiceProvider.GetRequiredService<CurrentUserContext>().SetUser(Guid.NewGuid(), organizationId, [new RoleAssignment(Role.Admin)], null);

        var result = await scope.ServiceProvider.GetRequiredService<ISender>().Send(new CloseDueRfqsCommand());

        result.Error!.Code.Should().Be("system.only");
    }

    [Fact]
    public async Task The_runner_visits_every_organization()
    {
        var first = await SourcingFlow.StartAsync(factory, supplierCount: 1);
        var second = await SourcingFlow.StartAsync(factory, supplierCount: 1);
        await PublishSingleSourceAsync(first);
        await PublishSingleSourceAsync(second);

        using var travel = factory.Clock.Travel(TimeSpan.FromDays(5));
        await using var scope = factory.Services.CreateAsyncScope();
        var closed = await scope.ServiceProvider.GetRequiredService<MawridJobs>().CloseDueRfqsAsync(CancellationToken.None);

        closed.Should().BeGreaterThanOrEqualTo(2);
        await factory.DeliverOutboxAsync();
    }

    [Fact]
    public async Task Clean_up_deletes_old_processed_outbox_messages_only()
    {
        var now = factory.Clock.UtcNow;
        var old = NewMessage(processedOn: now.AddDays(-8));
        var recent = NewMessage(processedOn: now.AddDays(-1));
        var pending = NewMessage(processedOn: null);
        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            db.OutboxMessages.AddRange(old, recent, pending);
            await db.SaveChangesAsync();
        }

        await using (var scope = factory.Services.CreateAsyncScope())
        {
            await scope.ServiceProvider.GetRequiredService<MawridJobs>().CleanUpAsync(CancellationToken.None);
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var ids = new[] { old.Id, recent.Id, pending.Id };
            db.OutboxMessages.Where(m => ids.Contains(m.Id)).Select(m => m.Id).ToList().Should().BeEquivalentTo([recent.Id, pending.Id]);
        }
    }

    [Fact]
    public void Tests_run_without_a_hangfire_server_and_every_job_has_a_schedule()
    {
        factory.Services.GetServices<IHostedService>().Should().NotContain(s => s.GetType().FullName!.StartsWith("Hangfire"));
        RecurringJobSchedule.Jobs.Select(j => j.Id).Should().OnlyHaveUniqueItems().And.HaveCount(4);
        RecurringJobSchedule.Riyadh.BaseUtcOffset.Should().Be(TimeSpan.FromHours(3));
    }

    private static OutboxMessage NewMessage(DateTimeOffset? processedOn) => new()
    {
        Id = Guid.NewGuid(),
        Type = "Test.Cleanup",
        Content = "{}",
        OccurredOnUtc = DateTimeOffset.UtcNow.AddDays(-10),
        NextAttemptAtUtc = processedOn is null ? DateTimeOffset.UtcNow.AddYears(10) : DateTimeOffset.UtcNow.AddDays(-10),
        ProcessedOnUtc = processedOn,
    };

    /// <summary>Publishes an RFQ to the flow's only supplier, with a single-source reason.</summary>
    private static async Task PublishSingleSourceAsync(SourcingFlow flow)
    {
        var rfqId = await flow.CreateRfqAsync();
        (await flow.Buyer.PostAsJsonAsync($"/api/v1/rfqs/{rfqId}/invitations", new { supplierIds = new[] { flow.ApprovedSuppliers[0].Id } }))
            .StatusCode.Should().Be(HttpStatusCode.NoContent);
        (await flow.Buyer.PutAsJsonAsync($"/api/v1/rfqs/{rfqId}/deadline", new { deadline = DateTimeOffset.UtcNow.AddDays(4) }))
            .StatusCode.Should().Be(HttpStatusCode.NoContent);
        (await flow.Buyer.PostAsJsonAsync($"/api/v1/rfqs/{rfqId}/publish", new { singleSourceReason = "Only authorised dealer in the region" }))
            .StatusCode.Should().Be(HttpStatusCode.NoContent);
    }

    private static async Task<string> EmailOfAsync(HttpClient client) =>
        (await (await client.GetAsync("/api/v1/me")).ShouldBeOkAsync<UserDto>()).Email;
}
