using Mawrid.Domain.Common;
using Mawrid.Infrastructure.Identity;
using Mawrid.Infrastructure.Outbox;
using MediatR;
using Microsoft.Extensions.DependencyInjection;

namespace Mawrid.Api.Tests.Infrastructure;

/// <summary>Runs background work by hand: tests turn off the outbox worker and Hangfire.</summary>
public static class BackgroundWork
{
    /// <summary>Delivers every outbox message that is due, like the background worker would.</summary>
    public static async Task DeliverOutboxAsync(this MawridApiFactory factory)
    {
        var processor = factory.Services.GetRequiredService<OutboxProcessor>();
        while (await processor.ProcessBatchAsync(CancellationToken.None) > 0)
        {
        }
    }

    /// <summary>Sends a command the way a background job does: as the system user of one organization.</summary>
    public static async Task<Result<T>> RunAsSystemAsync<T>(this MawridApiFactory factory, Guid organizationId, IRequest<Result<T>> command)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        scope.ServiceProvider.GetRequiredService<CurrentUserContext>().RunAsSystem(organizationId);
        return await scope.ServiceProvider.GetRequiredService<ISender>().Send(command);
    }
}
