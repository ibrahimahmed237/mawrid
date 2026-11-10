namespace Mawrid.Api.Common;

/// <summary>
/// ETag helpers for optimistic concurrency. The ETag is the row version in quotes,
/// for example <c>"AAAAAAAAB9E="</c>. Clients send it back in <c>If-Match</c>.
/// </summary>
public static class ETags
{
    public static string Format(string version) => $"\"{version}\"";

    /// <summary>The version from the If-Match header, without quotes, or null when absent.</summary>
    public static string? FromIfMatch(HttpRequest request)
    {
        var value = request.Headers.IfMatch.ToString();
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        value = value.Trim();
        if (value.StartsWith("W/", StringComparison.Ordinal))
        {
            value = value[2..];
        }

        return value.Trim('"');
    }
}
