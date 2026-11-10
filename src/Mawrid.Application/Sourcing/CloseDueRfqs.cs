using Mawrid.Application.Common.Abstractions;
using Mawrid.Application.Common.Messaging;
using Mawrid.Application.Common.Persistence;
using Mawrid.Application.Common.Security;
using Mawrid.Domain.Common;
using Mawrid.Domain.Sourcing;
using Microsoft.EntityFrameworkCore;

namespace Mawrid.Application.Sourcing;

/// <summary>
/// System job, per organization: closes published RFQs whose deadline passed, so the buyer is told.
/// Submissions are already refused after the deadline (the domain checks the clock), so a late run is harmless.
/// </summary>
public sealed record CloseDueRfqsCommand : ICommand<int>;

internal sealed class CloseDueRfqsCommandHandler(IAppDbContext db, ICurrentUser currentUser, IClock clock) : ICommandHandler<CloseDueRfqsCommand, int>
{
    private const int BatchSize = 200;

    public async Task<Result<int>> Handle(CloseDueRfqsCommand command, CancellationToken cancellationToken)
    {
        if (!currentUser.IsSystem)
        {
            return SystemErrors.SystemOnly;
        }

        var now = clock.UtcNow;
        var due = await db.Rfqs
            .Where(r => r.Status == RfqStatus.Published && r.Deadline <= now)
            .OrderBy(r => r.Deadline)
            .Take(BatchSize)
            .ToListAsync(cancellationToken);

        foreach (var rfq in due)
        {
            rfq.Close(now);
        }

        await db.SaveChangesAsync(cancellationToken);
        return due.Count;
    }
}
