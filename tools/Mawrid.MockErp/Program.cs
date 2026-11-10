using System.Collections.Concurrent;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;

// A tiny "ERP" that receives Mawrid webhooks. It checks the signature exactly as a real receiver should:
// sha256=HEX(HMAC-SHA256(secret, "{X-Mawrid-Timestamp}.{body}")), timestamp at most 5 minutes old,
// and it ignores a delivery id it has already seen.
var builder = WebApplication.CreateBuilder(args);
var secret = builder.Configuration["MOCKERP_SECRET"] ?? "whsec_local-development-secret-0123456789";
var app = builder.Build();

var received = new ConcurrentQueue<object>();
var seen = new ConcurrentDictionary<string, bool>();

app.MapPost("/webhooks", async (HttpRequest request, ILogger<Program> logger) =>
{
    var body = await new StreamReader(request.Body, Encoding.UTF8).ReadToEndAsync();
    var timestampText = request.Headers["X-Mawrid-Timestamp"].ToString();
    var signature = request.Headers["X-Mawrid-Signature"].ToString();
    var delivery = request.Headers["X-Mawrid-Delivery"].ToString();

    if (!long.TryParse(timestampText, NumberStyles.Integer, CultureInfo.InvariantCulture, out var timestamp)
        || Math.Abs(DateTimeOffset.UtcNow.ToUnixTimeSeconds() - timestamp) > 300)
    {
        return Results.Unauthorized();
    }

    var expected = "sha256=" + Convert.ToHexStringLower(
        HMACSHA256.HashData(Encoding.UTF8.GetBytes(secret), Encoding.UTF8.GetBytes($"{timestamp}.{body}")));
    if (!CryptographicOperations.FixedTimeEquals(Encoding.ASCII.GetBytes(expected), Encoding.ASCII.GetBytes(signature)))
    {
        logger.LogWarning("Rejected webhook {Delivery}: bad signature", delivery);
        return Results.Unauthorized();
    }

    if (!seen.TryAdd(delivery, true))
    {
        return Results.Ok(new { duplicate = true });
    }

    logger.LogInformation("Received {Event} ({Delivery}): {Body}", request.Headers["X-Mawrid-Event"].ToString(), delivery, body);
    received.Enqueue(new { delivery, eventType = request.Headers["X-Mawrid-Event"].ToString(), receivedAtUtc = DateTimeOffset.UtcNow, body });
    while (received.Count > 100 && received.TryDequeue(out _))
    {
    }

    return Results.NoContent();
});

app.MapGet("/webhooks", () => received.Reverse());
app.MapGet("/", () => "Mawrid mock ERP. POST /webhooks, GET /webhooks.");

app.Run();
