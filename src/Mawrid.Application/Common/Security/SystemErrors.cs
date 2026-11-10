using Mawrid.Domain.Common;

namespace Mawrid.Application.Common.Security;

public static class SystemErrors
{
    /// <summary>Returned when a person (not a background job) sends a system command.</summary>
    public static readonly Error SystemOnly = Error.Forbidden("system.only", "Only background jobs can run this command.");
}
