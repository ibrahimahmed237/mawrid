using System.Globalization;
using System.Threading.RateLimiting;
using Mawrid.Infrastructure.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.Extensions.Options;

namespace Mawrid.Api.Common.Security;

/// <summary>Limits per minute (section "RateLimits"). Tests raise them; one test lowers them.</summary>
public sealed class RateLimitOptions
{
    public const string SectionName = "RateLimits";

    /// <summary>Login and refresh attempts per client IP.</summary>
    public int LoginPerMinute { get; set; } = 10;

    /// <summary>Supplier portal requests per supplier user.</summary>
    public int SupplierPerMinute { get; set; } = 120;

    /// <summary>AI extractions per user. Each one costs money or quota at the provider.</summary>
    public int AiExtractionPerMinute { get; set; } = 10;
}

/// <summary>
/// Rate limits for the endpoints that attract abuse: login (password guessing), the supplier portal
/// (outside users) and AI extraction (cost). Over the limit gives 429 with <c>Retry-After</c>.
/// </summary>
public static class RateLimits
{
    public const string Login = "login";
    public const string SupplierPortal = "supplier-portal";
    public const string AiExtraction = "ai-extraction";

    public static IServiceCollection AddApiRateLimiting(this IServiceCollection services)
    {
        services.AddOptions<RateLimitOptions>().BindConfiguration(RateLimitOptions.SectionName);
        services.AddRateLimiter(options =>
        {
            options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
            options.OnRejected = WriteProblemAsync;

            options.AddPolicy(Login, http => FixedWindow($"ip:{http.Connection.RemoteIpAddress}", Limits(http).LoginPerMinute));
            options.AddPolicy(SupplierPortal, http => FixedWindow(UserOrIp(http), Limits(http).SupplierPerMinute));
            options.AddPolicy(AiExtraction, http => FixedWindow(UserOrIp(http), Limits(http).AiExtractionPerMinute));
        });
        return services;
    }

    private static RateLimitOptions Limits(HttpContext http) => http.RequestServices.GetRequiredService<IOptions<RateLimitOptions>>().Value;

    private static string UserOrIp(HttpContext http) =>
        http.User.FindFirst(MawridClaims.Subject)?.Value is { } user ? $"user:{user}" : $"ip:{http.Connection.RemoteIpAddress}";

    private static RateLimitPartition<string> FixedWindow(string key, int permitsPerMinute) =>
        RateLimitPartition.GetFixedWindowLimiter(key, _ => new FixedWindowRateLimiterOptions
        {
            PermitLimit = permitsPerMinute,
            Window = TimeSpan.FromMinutes(1),
            QueueLimit = 0,
        });

    private static async ValueTask WriteProblemAsync(OnRejectedContext context, CancellationToken cancellationToken)
    {
        var http = context.HttpContext;
        if (context.Lease.TryGetMetadata(MetadataName.RetryAfter, out var retryAfter))
        {
            http.Response.Headers.RetryAfter = ((int)Math.Ceiling(retryAfter.TotalSeconds)).ToString(CultureInfo.InvariantCulture);
        }

        await http.RequestServices.GetRequiredService<IProblemDetailsService>().WriteAsync(new ProblemDetailsContext
        {
            HttpContext = http,
            ProblemDetails = new ProblemDetails
            {
                Status = StatusCodes.Status429TooManyRequests,
                Title = "Too many requests",
                Detail = "Please wait and try again.",
            },
        });
    }
}
