using Hangfire;
using Hangfire.SqlServer;
using Mawrid.Application.Common.Abstractions;
using Mawrid.Application.Common.Notifications;
using Mawrid.Application.Common.Persistence;
using Mawrid.Application.Common.Security;
using Mawrid.Application.Files;
using Mawrid.Infrastructure.Files;
using Mawrid.Infrastructure.Identity;
using Mawrid.Infrastructure.Jobs;
using Mawrid.Infrastructure.Notifications;
using Mawrid.Infrastructure.Outbox;
using Mawrid.Infrastructure.Persistence;
using Mawrid.Infrastructure.Persistence.Interceptors;
using Mawrid.Infrastructure.Seeding;
using Mawrid.Infrastructure.Time;
using Microsoft.AspNetCore.Identity;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Mawrid.Infrastructure;

public static class DependencyInjection
{
    public const string ConnectionStringName = "Default";

    /// <summary>Registers the database, identity, outbox, clock and other infrastructure services.</summary>
    public static IServiceCollection AddInfrastructure(this IServiceCollection services, IConfiguration configuration)
    {
        var connectionString = configuration.GetConnectionString(ConnectionStringName);
        if (string.IsNullOrWhiteSpace(connectionString))
        {
            throw new InvalidOperationException(
                $"Connection string '{ConnectionStringName}' is missing. " +
                $"Set it with user-secrets or the environment variable ConnectionStrings__{ConnectionStringName}.");
        }

        services.AddSingleton(TimeProvider.System);
        services.AddSingleton<IClock, SystemClock>();

        services.AddScoped<CurrentUserContext>();
        services.AddScoped<ICurrentUser>(sp => sp.GetRequiredService<CurrentUserContext>());

        services.AddScoped<OutboxInterceptor>();
        services.AddScoped<AuditingInterceptor>();
        services.AddDbContext<AppDbContext>((sp, options) => options
            .UseSqlServer(connectionString, sql => sql.EnableRetryOnFailure())
            // Order matters: events go to the outbox first, then every change is audited.
            .AddInterceptors(sp.GetRequiredService<OutboxInterceptor>(), sp.GetRequiredService<AuditingInterceptor>()));
        services.AddScoped<IAppDbContext>(sp => sp.GetRequiredService<AppDbContext>());

        services.AddIdentityCore<UserCredential>(options =>
            {
                options.User.RequireUniqueEmail = true;
                options.Password.RequiredLength = 10;
                options.Password.RequireNonAlphanumeric = false;
                options.Lockout.MaxFailedAccessAttempts = 5;
                options.Lockout.DefaultLockoutTimeSpan = TimeSpan.FromMinutes(15);
            })
            .AddEntityFrameworkStores<AppDbContext>();
        services.AddScoped<IIdentityService, IdentityService>();

        services.AddOptions<JwtOptions>().BindConfiguration(JwtOptions.SectionName).ValidateDataAnnotations().ValidateOnStart();
        services.AddSingleton<ITokenService, JwtTokenService>();

        services.AddOptions<OutboxOptions>().BindConfiguration(OutboxOptions.SectionName).ValidateDataAnnotations().ValidateOnStart();
        services.AddSingleton<OutboxProcessor>();
        services.AddHostedService<OutboxWorker>();

        services.AddOptions<SeedOptions>().BindConfiguration(SeedOptions.SectionName);

        services.AddOptions<FileStorageOptions>().BindConfiguration(FileStorageOptions.SectionName).ValidateDataAnnotations().ValidateOnStart();
        services.AddSingleton<IFileStorage, AzureBlobFileStorage>();
        services.AddSingleton<IMalwareScanner, EicarTestScanner>();

        services.AddSingleton<Idempotency.IdempotencyStore>();
        services.AddScoped<IDocumentNumbers, Numbering.DocumentNumbers>();

        // Notifications: every channel gets every notification. The API adds the real-time channel.
        services.AddScoped<INotificationSender, NotificationSender>();
        services.AddScoped<INotificationChannel, EmailNotificationChannel>();
        services.AddSingleton<IEmailSender, LogEmailSender>();

        AddJobs(services, configuration, connectionString);
        return services;
    }

    /// <summary>Hangfire keeps its queues in the same database, in the "hangfire" schema.</summary>
    private static void AddJobs(IServiceCollection services, IConfiguration configuration, string connectionString)
    {
        services.AddOptions<JobsOptions>().BindConfiguration(JobsOptions.SectionName);
        services.AddSingleton<TenantJobRunner>();
        services.AddScoped<MawridJobs>();

        if (!configuration.GetValue(JobsOptions.SectionName + ":" + nameof(JobsOptions.Enabled), defaultValue: true))
        {
            return;
        }

        services.AddHangfire(config => config
            .SetDataCompatibilityLevel(CompatibilityLevel.Version_180)
            .UseSimpleAssemblyNameTypeSerializer()
            .UseRecommendedSerializerSettings()
            .UseSqlServerStorage(() => new SqlConnection(connectionString), new SqlServerStorageOptions
            {
                SchemaName = "hangfire",
                PrepareSchemaIfNecessary = true,
            }));
        services.AddHangfireServer(options => options.WorkerCount = 2);
        services.AddHostedService<RecurringJobRegistration>();
    }
}
