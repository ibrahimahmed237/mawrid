using Microsoft.AspNetCore.Hosting;
using Microsoft.Data.SqlClient;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Testcontainers.MsSql;

namespace Mawrid.Api.Tests.Infrastructure;

/// <summary>
/// Starts the real API in memory against a real SQL Server in Docker.
/// One container is shared by all tests in the "api" collection.
/// </summary>
public sealed class MawridApiFactory : WebApplicationFactory<Program>, IAsyncLifetime
{
    public const string SqlServerImage = "mcr.microsoft.com/mssql/server:2022-latest";

    private readonly MsSqlContainer _sqlServer = new MsSqlBuilder(SqlServerImage).Build();

    /// <summary>Points at a test database (not master), created by the migrations on startup.</summary>
    public string ConnectionString => new SqlConnectionStringBuilder(_sqlServer.GetConnectionString())
    {
        InitialCatalog = "MawridTests",
    }.ConnectionString;

    public Task InitializeAsync() => _sqlServer.StartAsync();

    public new async Task DisposeAsync()
    {
        await base.DisposeAsync();
        await _sqlServer.DisposeAsync();
    }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Testing");
        builder.UseSetting("ConnectionStrings:Default", ConnectionString);
        builder.UseSetting("Database:MigrateOnStartup", "true");
        builder.ConfigureTestServices(services =>
            services.AddControllers().AddApplicationPart(typeof(MawridApiFactory).Assembly));
    }
}

[CollectionDefinition(Name)]
public sealed class ApiCollection : ICollectionFixture<MawridApiFactory>
{
    public const string Name = "api";
}
