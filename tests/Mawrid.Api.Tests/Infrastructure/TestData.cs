using Mawrid.Domain.Common;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using Mawrid.Application.Common.Security;
using Mawrid.Application.Identity.Auth;
using Mawrid.Domain.Identity;
using Mawrid.Infrastructure.Identity;
using Mawrid.Infrastructure.Persistence;
using Microsoft.Extensions.DependencyInjection;

namespace Mawrid.Api.Tests.Infrastructure;

/// <summary>Creates organizations and users straight in the database, and logs them in.</summary>
public static class TestData
{
    public const string Password = "Test-Passw0rd-2026";

    public static string UniqueEmail(string prefix = "user") => $"{prefix}-{Guid.NewGuid():N}@test.sa";

    public static async Task<Guid> CreateOrganizationAsync(this MawridApiFactory factory, string name = "Test Org")
    {
        await using var scope = factory.Services.CreateAsyncScope();
        scope.ServiceProvider.GetRequiredService<CurrentUserContext>().RunAsSystem(null);
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var organization = Organization.Create(name).Value;
        db.Organizations.Add(organization);
        await db.SaveChangesAsync();
        return organization.Id;
    }

    public static async Task<Guid> CreateCostCenterAsync(this MawridApiFactory factory, Guid organizationId, string code)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        scope.ServiceProvider.GetRequiredService<CurrentUserContext>().RunAsSystem(organizationId);
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var costCenter = CostCenter.Create(organizationId, code, $"Cost center {code}").Value;
        db.CostCenters.Add(costCenter);
        await db.SaveChangesAsync();
        return costCenter.Id;
    }

    public static async Task<Guid> CreateUserAsync(
        this MawridApiFactory factory, Guid organizationId, string email, Guid? supplierId = null, params RoleAssignment[] roles)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        scope.ServiceProvider.GetRequiredService<CurrentUserContext>().RunAsSystem(organizationId);
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var identity = scope.ServiceProvider.GetRequiredService<IIdentityService>();

        var user = User.Create(organizationId, email, "Test User", roles, supplierId).Value;
        db.Users.Add(user);
        var result = await identity.CreateCredentialAsync(user.Id, user.Email, Password, CancellationToken.None);
        result.IsSuccess.Should().BeTrue(result.Error?.Message);
        return user.Id;
    }

    public static Task<Guid> CreateUserAsync(this MawridApiFactory factory, Guid organizationId, string email, params Role[] roles) =>
        factory.CreateUserAsync(organizationId, email, null, roles.Select(r => new RoleAssignment(r)).ToArray());

    public static async Task<TokenPairDto> LoginAsync(this HttpClient client, string email, string password = Password)
    {
        var response = await client.PostAsJsonAsync("/api/v1/auth/login", new { email, password });
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<TokenPairDto>())!;
    }

    /// <summary>A client that sends the user's access token on every request.</summary>
    public static async Task<HttpClient> CreateClientForAsync(this MawridApiFactory factory, string email)
    {
        var client = factory.CreateClient();
        var tokens = await client.LoginAsync(email);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", tokens.AccessToken);
        return client;
    }
}
