using System.Text.Json;
using Mawrid.Infrastructure.Persistence;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace Mawrid.Api.Common.Health;

/// <summary>
/// Two health endpoints, as used by Docker and Kubernetes:
/// <list type="bullet">
/// <item><c>/health/live</c>: the process is running. No dependencies are checked.</item>
/// <item><c>/health/ready</c>: the app can serve traffic (the database answers).</item>
/// </list>
/// </summary>
public static class HealthSetup
{
    private const string ReadyTag = "ready";

    public static IServiceCollection AddApiHealthChecks(this IServiceCollection services)
    {
        services.AddHealthChecks()
            .AddDbContextCheck<AppDbContext>("sql-server", tags: [ReadyTag]);
        return services;
    }

    public static IEndpointRouteBuilder MapApiHealthChecks(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapHealthChecks("/health/live", new HealthCheckOptions
        {
            Predicate = _ => false,
            ResponseWriter = WriteJson,
        });

        endpoints.MapHealthChecks("/health/ready", new HealthCheckOptions
        {
            Predicate = check => check.Tags.Contains(ReadyTag),
            ResponseWriter = WriteJson,
        });

        return endpoints;
    }

    private static Task WriteJson(HttpContext context, HealthReport report)
    {
        context.Response.ContentType = "application/json";
        var body = new
        {
            status = report.Status.ToString(),
            checks = report.Entries.Select(e => new
            {
                name = e.Key,
                status = e.Value.Status.ToString(),
                durationMs = Math.Round(e.Value.Duration.TotalMilliseconds, 1),
            }),
        };
        return context.Response.WriteAsync(JsonSerializer.Serialize(body, JsonSerializerOptions.Web));
    }
}
