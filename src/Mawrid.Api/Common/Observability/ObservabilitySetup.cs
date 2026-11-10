using OpenTelemetry;
using OpenTelemetry.Metrics;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;
using Serilog;
using Serilog.Formatting.Compact;

namespace Mawrid.Api.Common.Observability;

public static class ObservabilitySetup
{
    public const string ServiceName = "mawrid-api";

    /// <summary>
    /// Structured JSON logs with Serilog. Levels can be changed in appsettings
    /// under <c>Serilog:MinimumLevel</c>.
    /// </summary>
    public static IServiceCollection AddApiLogging(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddSerilog((_, logger) => logger
            .ReadFrom.Configuration(configuration)
            .Enrich.FromLogContext()
            .Enrich.WithProperty("Service", ServiceName)
            .WriteTo.Console(new RenderedCompactJsonFormatter()));
        return services;
    }

    /// <summary>
    /// Traces and metrics with OpenTelemetry. Data is exported over OTLP only when
    /// <c>OTEL_EXPORTER_OTLP_ENDPOINT</c> is set, so local runs and tests stay quiet.
    /// </summary>
    public static IServiceCollection AddApiTelemetry(this IServiceCollection services, IConfiguration configuration)
    {
        var otel = services.AddOpenTelemetry()
            .ConfigureResource(resource => resource.AddService(ServiceName))
            .WithTracing(tracing => tracing
                .AddAspNetCoreInstrumentation(options =>
                    options.Filter = context => !context.Request.Path.StartsWithSegments("/health"))
                .AddHttpClientInstrumentation())
            .WithMetrics(metrics => metrics
                .AddAspNetCoreInstrumentation()
                .AddHttpClientInstrumentation());

        if (!string.IsNullOrWhiteSpace(configuration["OTEL_EXPORTER_OTLP_ENDPOINT"]))
        {
            otel.UseOtlpExporter();
        }

        return services;
    }
}
