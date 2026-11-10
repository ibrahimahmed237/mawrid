using Mawrid.Application.Common.Abstractions;
using Mawrid.Infrastructure.Identity;
using Mawrid.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Mawrid.Infrastructure.Numbering;

/// <summary>
/// Gives numbers like PR-2026-000012. One atomic MERGE increments the counter, with
/// HOLDLOCK so two requests never get the same number. The year is the Riyadh business year.
/// </summary>
public sealed class DocumentNumbers(AppDbContext db, CurrentUserContext currentUser, IClock clock) : IDocumentNumbers
{
    public async Task<string> NextAsync(string prefix, CancellationToken cancellationToken)
    {
        var organizationId = currentUser.OrganizationId
            ?? throw new InvalidOperationException("Document numbers need an organization context.");
        var year = clock.Today.Year;

        var next = await db.Database.SqlQuery<int>($"""
            MERGE platform.DocumentSequences WITH (HOLDLOCK) AS target
            USING (SELECT {organizationId} AS OrganizationId, {prefix} AS Prefix, {year} AS [Year]) AS source
            ON target.OrganizationId = source.OrganizationId AND target.Prefix = source.Prefix AND target.[Year] = source.[Year]
            WHEN MATCHED THEN UPDATE SET LastValue = target.LastValue + 1
            WHEN NOT MATCHED THEN INSERT (OrganizationId, Prefix, [Year], LastValue) VALUES (source.OrganizationId, source.Prefix, source.[Year], 1)
            OUTPUT inserted.LastValue AS [Value];
            """).ToListAsync(cancellationToken);

        return $"{prefix}-{year}-{next.Single():D6}";
    }
}
