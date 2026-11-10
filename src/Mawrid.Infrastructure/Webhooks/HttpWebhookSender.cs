using System.Net.Mime;
using System.Security.Cryptography;
using System.Text;
using Mawrid.Application.Integrations;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.Extensions.Options;

namespace Mawrid.Infrastructure.Webhooks;

/// <summary>Settings for outgoing webhooks (section "Webhooks").</summary>
public sealed class WebhookOptions : IWebhookSettings
{
    public const string SectionName = "Webhooks";

    public bool AllowHttp { get; set; }

    /// <summary>How long one call may take before it counts as failed.</summary>
    public TimeSpan Timeout { get; set; } = TimeSpan.FromSeconds(10);
}

/// <summary>Posts the JSON body with the event, delivery id, timestamp and signature headers. Any 2xx is success.</summary>
internal sealed class HttpWebhookSender(HttpClient http, IOptions<WebhookOptions> options) : IWebhookSender
{
    public async Task<WebhookSendResult> SendAsync(WebhookRequest request, CancellationToken cancellationToken)
    {
        if (request.Url.Scheme != Uri.UriSchemeHttps && !options.Value.AllowHttp)
        {
            return new WebhookSendResult(false, null, "HTTPS is required.");
        }

        var timestamp = request.Timestamp.ToUnixTimeSeconds();
        using var message = new HttpRequestMessage(HttpMethod.Post, request.Url)
        {
            Content = new StringContent(request.Payload, Encoding.UTF8, MediaTypeNames.Application.Json),
        };
        message.Headers.Add(WebhookSignature.EventHeader, request.EventType);
        message.Headers.Add(WebhookSignature.DeliveryHeader, request.DeliveryId.ToString());
        message.Headers.Add(WebhookSignature.TimestampHeader, timestamp.ToString(System.Globalization.CultureInfo.InvariantCulture));
        message.Headers.Add(WebhookSignature.SignatureHeader, WebhookSignature.Compute(request.Secret, timestamp, request.Payload));

        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(options.Value.Timeout);
        try
        {
            using var response = await http.SendAsync(message, timeout.Token);
            var status = (int)response.StatusCode;
            return response.IsSuccessStatusCode
                ? new WebhookSendResult(true, status, null)
                : new WebhookSendResult(false, status, $"HTTP {status} {response.ReasonPhrase}");
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return new WebhookSendResult(false, null, $"No answer within {options.Value.Timeout.TotalSeconds:0} seconds.");
        }
        catch (HttpRequestException exception)
        {
            return new WebhookSendResult(false, null, exception.Message);
        }
    }
}

/// <summary>Secrets are random 32-byte values; at rest they are encrypted with ASP.NET Core Data Protection.</summary>
internal sealed class DataProtectionWebhookSecrets(IDataProtectionProvider provider) : IWebhookSecrets
{
    private readonly IDataProtector _protector = provider.CreateProtector("Mawrid.Webhooks.Secret.v1");

    public string Generate() => "whsec_" + Convert.ToBase64String(RandomNumberGenerator.GetBytes(32)).TrimEnd('=').Replace('+', '-').Replace('/', '_');

    public string Protect(string secret) => _protector.Protect(secret);

    public string Unprotect(string protectedSecret) => _protector.Unprotect(protectedSecret);
}
