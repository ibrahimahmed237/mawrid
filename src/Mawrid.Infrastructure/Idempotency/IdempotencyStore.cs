using Mawrid.Application.Common.Abstractions;
using Mawrid.Infrastructure.Identity;
using Mawrid.Infrastructure.Persistence;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Mawrid.Infrastructure.Idempotency;

public enum IdempotencyOutcome
{
    /// <summary>First time we see this key: run the request.</summary>
    Started,

    /// <summary>Same key and same request, already finished: return the saved response.</summary>
    Replay,

    /// <summary>Same key, and the first request is still running.</summary>
    InProgress,

    /// <summary>Same key, but a different request. The client has a bug.</summary>
    KeyReused,
}

public sealed record IdempotencyBegin(IdempotencyOutcome Outcome, IdempotencyRecord? Saved = null);

/// <summary>
/// Reads and writes idempotency records in their own DbContext scope, so they never mix
/// with the changes of the request itself.
/// </summary>
public sealed class IdempotencyStore(IServiceScopeFactory scopeFactory, IClock clock)
{
    public static readonly TimeSpan Retention = TimeSpan.FromHours(24);

    public async Task<IdempotencyBegin> BeginAsync(Guid organizationId, string key, string requestHash, CancellationToken cancellationToken)
    {
        await using var scope = CreateScope(organizationId, out var db);
        var now = clock.UtcNow;

        // An expired record is gone for good; the key can be used again.
        await db.IdempotencyRecords
            .Where(r => r.OrganizationId == organizationId && r.Key == key && r.ExpiresAtUtc <= now)
            .ExecuteDeleteAsync(cancellationToken);

        db.IdempotencyRecords.Add(new IdempotencyRecord
        {
            Id = Guid.CreateVersion7(),
            OrganizationId = organizationId,
            Key = key,
            RequestHash = requestHash,
            CreatedAtUtc = now,
            ExpiresAtUtc = now + Retention,
        });

        try
        {
            await db.SaveChangesAsync(cancellationToken);
            return new IdempotencyBegin(IdempotencyOutcome.Started);
        }
        catch (DbUpdateException ex) when (IsUniqueViolation(ex))
        {
            db.ChangeTracker.Clear();
            var saved = await db.IdempotencyRecords.AsNoTracking()
                .SingleAsync(r => r.OrganizationId == organizationId && r.Key == key, cancellationToken);

            if (saved.RequestHash != requestHash)
            {
                return new IdempotencyBegin(IdempotencyOutcome.KeyReused);
            }

            return saved.CompletedAtUtc is null
                ? new IdempotencyBegin(IdempotencyOutcome.InProgress)
                : new IdempotencyBegin(IdempotencyOutcome.Replay, saved);
        }
    }

    public async Task CompleteAsync(
        Guid organizationId, string key, int statusCode, string? contentType, string? location, byte[] body, CancellationToken cancellationToken)
    {
        await using var scope = CreateScope(organizationId, out var db);
        var now = clock.UtcNow;
        await db.IdempotencyRecords
            .Where(r => r.OrganizationId == organizationId && r.Key == key)
            .ExecuteUpdateAsync(s => s
                .SetProperty(r => r.CompletedAtUtc, now)
                .SetProperty(r => r.StatusCode, statusCode)
                .SetProperty(r => r.ResponseContentType, contentType)
                .SetProperty(r => r.ResponseLocation, location)
                .SetProperty(r => r.ResponseBody, body), cancellationToken);
    }

    /// <summary>Forgets the key after a server error, so the client can safely retry.</summary>
    public async Task AbandonAsync(Guid organizationId, string key, CancellationToken cancellationToken)
    {
        await using var scope = CreateScope(organizationId, out var db);
        await db.IdempotencyRecords
            .Where(r => r.OrganizationId == organizationId && r.Key == key && r.CompletedAtUtc == null)
            .ExecuteDeleteAsync(cancellationToken);
    }

    private AsyncServiceScope CreateScope(Guid organizationId, out AppDbContext db)
    {
        var scope = scopeFactory.CreateAsyncScope();
        scope.ServiceProvider.GetRequiredService<CurrentUserContext>().RunAsSystem(organizationId);
        db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        return scope;
    }

    private static bool IsUniqueViolation(DbUpdateException ex) =>
        ex.InnerException is SqlException { Number: 2601 or 2627 };
}
