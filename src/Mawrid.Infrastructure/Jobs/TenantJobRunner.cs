using Mawrid.Domain.Common;
using Mawrid.Infrastructure.Identity;
using Mawrid.Infrastructure.Persistence;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Mawrid.Infrastructure.Jobs;

/// <summary>
/// Runs a system command once for every organization, each in its own scope and as the system user
/// for that organization. The tenant filter then works as in a normal request. A failure in one
/// organization is logged and does not stop the others.
/// </summary>
public sealed partial class TenantJobRunner(IServiceScopeFactory scopes, ILogger<TenantJobRunner> logger)
{
    /// <returns>The sum of the counts the command returned.</returns>
    public async Task<int> RunForEachOrganizationAsync<TCommand>(Func<TCommand> createCommand, CancellationToken cancellationToken)
        where TCommand : IRequest<Result<int>>
    {
        var jobName = typeof(TCommand).Name;
        var organizationIds = await ListOrganizationsAsync(cancellationToken);
        var total = 0;
        var failures = 0;

        foreach (var organizationId in organizationIds)
        {
            await using var scope = scopes.CreateAsyncScope();
            var context = scope.ServiceProvider.GetRequiredService<CurrentUserContext>();
            context.RunAsSystem(organizationId);
            context.SetCorrelationId($"job-{Guid.NewGuid():N}");

            try
            {
                var result = await scope.ServiceProvider.GetRequiredService<ISender>().Send(createCommand(), cancellationToken);
                if (result.IsFailure)
                {
                    failures++;
                    LogFailed(null, jobName, organizationId, result.Error!.Code);
                    continue;
                }

                total += result.Value;
            }
            catch (Exception exception) when (exception is not OperationCanceledException)
            {
                failures++;
                LogFailed(exception, jobName, organizationId, exception.GetType().Name);
            }
        }

        LogFinished(jobName, organizationIds.Count, total, failures);
        if (failures > 0)
        {
            // Marks the job run as failed in Hangfire, so it shows up on the dashboard.
            throw new InvalidOperationException($"{jobName} failed for {failures} of {organizationIds.Count} organizations.");
        }

        return total;
    }

    private async Task<List<Guid>> ListOrganizationsAsync(CancellationToken cancellationToken)
    {
        await using var scope = scopes.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        return await db.Organizations.AsNoTracking().OrderBy(o => o.Id).Select(o => o.Id).ToListAsync(cancellationToken);
    }

    [LoggerMessage(Level = LogLevel.Error, Message = "Job {Job} failed for organization {OrganizationId}: {Reason}")]
    private partial void LogFailed(Exception? exception, string job, Guid organizationId, string reason);

    [LoggerMessage(Level = LogLevel.Information, Message = "Job {Job} finished for {Organizations} organizations: {Count} items, {Failures} failures")]
    private partial void LogFinished(string job, int organizations, int count, int failures);
}
