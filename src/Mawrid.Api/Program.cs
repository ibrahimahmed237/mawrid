using Mawrid.Api.Common.Correlation;
using Mawrid.Api.Common.Errors;
using Mawrid.Api.Common.Health;
using Mawrid.Api.Common.Idempotency;
using Mawrid.Api.Common.Observability;
using Mawrid.Api.Common.Security;
using Mawrid.Api.Notifications;
using Mawrid.Application;
using Mawrid.Infrastructure;
using Mawrid.Infrastructure.Persistence;
using Mawrid.Infrastructure.Seeding;
using Serilog;

var builder = WebApplication.CreateBuilder(args);

builder.Services
    .AddApiLogging(builder.Configuration)
    .AddApiTelemetry(builder.Configuration)
    .AddApiProblemDetails()
    .AddApplication()
    .AddInfrastructure(builder.Configuration)
    .AddApiHealthChecks()
    .AddApiAuthentication()
    .AddApiNotifications();

builder.Services.AddControllers();
builder.Services.AddOpenApi();

var app = builder.Build();

// Local development and tests only. Production runs migrations as a deploy step.
if (app.Configuration.GetValue<bool>("Database:MigrateOnStartup"))
{
    await app.Services.MigrateDatabaseAsync();
}

// Creates the first organization and admin when "Seed:Bootstrap" is configured. Does nothing if data exists.
await app.Services.SeedAsync();

// Order matters: the correlation id must exist before anything logs or fails.
app.UseMiddleware<CorrelationIdMiddleware>();
app.UseExceptionHandler();
app.UseStatusCodePages();
app.UseSerilogRequestLogging(options => options.GetLevel = RequestLogLevel.For);

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi().AllowAnonymous();
}

app.UseAuthentication();
app.UseMiddleware<CurrentUserMiddleware>();
app.UseAuthorization();
app.UseMiddleware<IdempotencyMiddleware>();

app.MapApiHealthChecks();
app.MapControllers();
app.MapHub<NotificationsHub>(NotificationsHub.Path);

await app.RunAsync();

/// <summary>Exposed so integration tests can start the app with WebApplicationFactory.</summary>
public partial class Program;
