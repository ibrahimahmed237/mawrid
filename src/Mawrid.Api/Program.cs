using Mawrid.Api.Common.Correlation;
using Mawrid.Api.Common.Errors;
using Mawrid.Api.Common.Health;
using Mawrid.Api.Common.Observability;
using Mawrid.Application;
using Mawrid.Infrastructure;
using Mawrid.Infrastructure.Persistence;
using Serilog;

var builder = WebApplication.CreateBuilder(args);

builder.Services
    .AddApiLogging(builder.Configuration)
    .AddApiTelemetry(builder.Configuration)
    .AddApiProblemDetails()
    .AddApplication()
    .AddInfrastructure(builder.Configuration)
    .AddApiHealthChecks();

builder.Services.AddControllers();
builder.Services.AddOpenApi();

var app = builder.Build();

// Local development and tests only. Production runs migrations as a deploy step.
if (app.Configuration.GetValue<bool>("Database:MigrateOnStartup"))
{
    await app.Services.MigrateDatabaseAsync();
}

// Order matters: the correlation id must exist before anything logs or fails.
app.UseMiddleware<CorrelationIdMiddleware>();
app.UseExceptionHandler();
app.UseStatusCodePages();
app.UseSerilogRequestLogging(options => options.GetLevel = RequestLogLevel.For);

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
}

app.MapApiHealthChecks();
app.MapControllers();

await app.RunAsync();

/// <summary>Exposed so integration tests can start the app with WebApplicationFactory.</summary>
public partial class Program;
