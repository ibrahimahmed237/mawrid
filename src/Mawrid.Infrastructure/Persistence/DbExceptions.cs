using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;

namespace Mawrid.Infrastructure.Persistence;

public static class DbExceptions
{
    /// <summary>
    /// True when SQL Server refused a row because of a unique index (errors 2601 and 2627). Handlers
    /// check for duplicates first; this catches two identical requests that arrive at the same moment.
    /// </summary>
    public static bool IsUniqueViolation(DbUpdateException exception) =>
        exception.InnerException is SqlException { Number: 2601 or 2627 };
}
