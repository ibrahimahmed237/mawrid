using System.Globalization;
using System.Security.Cryptography;
using System.Text;

namespace Mawrid.Application.Integrations;

/// <summary>Sends one webhook call. Implemented with a typed HttpClient in the infrastructure.</summary>
public interface IWebhookSender
{
    Task<WebhookSendResult> SendAsync(WebhookRequest request, CancellationToken cancellationToken);
}

public sealed record WebhookRequest(Uri Url, string EventType, Guid DeliveryId, string Payload, string Secret, DateTimeOffset Timestamp);

public sealed record WebhookSendResult(bool Success, int? StatusCode, string? Error);

/// <summary>Creates secrets and keeps them encrypted at rest.</summary>
public interface IWebhookSecrets
{
    string Generate();

    string Protect(string secret);

    string Unprotect(string protectedSecret);
}

public interface IWebhookSettings
{
    /// <summary>Allows plain HTTP URLs. Only for local development with the mock ERP.</summary>
    bool AllowHttp { get; }
}

/// <summary>
/// The signature in <c>X-Mawrid-Signature</c>: <c>sha256=</c> + hex HMAC-SHA256 of "{timestamp}.{body}".
/// The timestamp is in <c>X-Mawrid-Timestamp</c> (Unix seconds); receivers should refuse old ones to stop replays.
/// </summary>
public static class WebhookSignature
{
    public const string SignatureHeader = "X-Mawrid-Signature";
    public const string TimestampHeader = "X-Mawrid-Timestamp";
    public const string EventHeader = "X-Mawrid-Event";
    public const string DeliveryHeader = "X-Mawrid-Delivery";

    public static string Compute(string secret, long unixTimestamp, string payload)
    {
        var data = Encoding.UTF8.GetBytes(unixTimestamp.ToString(CultureInfo.InvariantCulture) + "." + payload);
        var hash = HMACSHA256.HashData(Encoding.UTF8.GetBytes(secret), data);
        return "sha256=" + Convert.ToHexStringLower(hash);
    }

    /// <summary>Compares in constant time, so the signature cannot be guessed byte by byte.</summary>
    public static bool Verify(string secret, long unixTimestamp, string payload, string signature) =>
        CryptographicOperations.FixedTimeEquals(
            Encoding.ASCII.GetBytes(Compute(secret, unixTimestamp, payload)), Encoding.ASCII.GetBytes(signature));
}
