namespace Mawrid.Application.Common.Persistence;

/// <summary>Helpers for the version a client sends back in <c>If-Match</c>.</summary>
public static class RowVersions
{
    /// <summary>The version as clients see it (Base64), also used for the ETag.</summary>
    public static string Format(byte[] rowVersion) => Convert.ToBase64String(rowVersion);

    /// <summary>True when the client sent a version and it is not the current one.</summary>
    public static bool IsStale(string? expectedVersion, byte[] rowVersion) =>
        expectedVersion is not null && expectedVersion != Format(rowVersion);
}
