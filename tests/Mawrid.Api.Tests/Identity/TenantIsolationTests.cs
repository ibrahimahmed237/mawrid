using System.Net;
using Mawrid.Api.Tests.Infrastructure;
using Mawrid.Application.Common.Paging;
using Mawrid.Application.Identity.CostCenters;
using Mawrid.Application.Identity.Users;
using Mawrid.Domain.Common;
using Mawrid.Domain.Identity;
using Mawrid.Infrastructure.Identity;
using Mawrid.Infrastructure.Persistence;
using Microsoft.Extensions.DependencyInjection;

namespace Mawrid.Api.Tests.Identity;

/// <summary>ADR-0003: one organization never sees or changes another's data.</summary>
[Collection(ApiCollection.Name)]
public class TenantIsolationTests(MawridApiFactory factory)
{
    [Fact]
    public async Task An_admin_sees_only_their_own_organization()
    {
        var orgA = await factory.CreateOrganizationAsync("Org A");
        var orgB = await factory.CreateOrganizationAsync("Org B");
        var adminA = TestData.UniqueEmail("admin-a");
        await factory.CreateUserAsync(orgA, adminA, Role.Admin);
        var userB = await factory.CreateUserAsync(orgB, TestData.UniqueEmail("user-b"), Role.Buyer);
        await factory.CreateCostCenterAsync(orgA, "A-001");
        await factory.CreateCostCenterAsync(orgB, "B-001");
        var client = await factory.CreateClientForAsync(adminA);

        var users = await (await client.GetAsync("/api/v1/users?pageSize=100")).ShouldBeOkAsync<PagedList<UserDto>>();
        var userOfB = await client.GetAsync($"/api/v1/users/{userB}");
        var deactivateUserOfB = await client.PostAsync($"/api/v1/users/{userB}/deactivate", null);
        var costCenters = await (await client.GetAsync("/api/v1/cost-centers")).ShouldBeOkAsync<List<CostCenterDto>>();

        users.Items.Should().ContainSingle().Which.Email.Should().Be(adminA);
        await userOfB.ShouldBeProblemAsync(HttpStatusCode.NotFound, "user.not_found");
        await deactivateUserOfB.ShouldBeProblemAsync(HttpStatusCode.NotFound, "user.not_found");
        costCenters.Select(c => c.Code).Should().Equal("A-001");
    }

    [Fact]
    public async Task Writing_data_of_another_organization_is_blocked()
    {
        var orgA = await factory.CreateOrganizationAsync("Org A");
        var orgB = await factory.CreateOrganizationAsync("Org B");
        await using var scope = factory.Services.CreateAsyncScope();
        scope.ServiceProvider.GetRequiredService<CurrentUserContext>().RunAsSystem(orgA);
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        db.CostCenters.Add(CostCenter.Create(orgB, "SNEAKY", "Not mine").Value);
        var save = () => db.SaveChangesAsync();

        await save.Should().ThrowAsync<InvalidOperationException>().WithMessage("*another organization*");
    }

    [Fact]
    public async Task Without_an_organization_context_tenant_data_is_invisible()
    {
        var orgA = await factory.CreateOrganizationAsync("Org A");
        await factory.CreateCostCenterAsync(orgA, "HIDDEN");
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        db.CostCenters.Should().BeEmpty();
    }

    [Fact]
    public async Task Supplier_users_cannot_use_internal_endpoints()
    {
        var orgA = await factory.CreateOrganizationAsync("Org A");
        var email = TestData.UniqueEmail("supplier");
        await factory.CreateUserAsync(orgA, email, Guid.NewGuid(), new RoleAssignment(Role.Supplier));
        var client = await factory.CreateClientForAsync(email);

        var response = await client.GetAsync("/api/v1/cost-centers");

        await response.ShouldBeProblemAsync(HttpStatusCode.Forbidden, "auth.forbidden");
    }
}
