using System.Net;
using System.Net.Http.Json;
using Mawrid.Api.Tests.Infrastructure;
using Mawrid.Application.Common.Paging;
using Mawrid.Application.Identity.Users;
using Mawrid.Domain.Identity;

namespace Mawrid.Api.Tests.Identity;

[Collection(ApiCollection.Name)]
public class UsersTests(MawridApiFactory factory)
{
    private async Task<(Guid OrganizationId, Guid AdminId, HttpClient Admin)> NewOrganizationWithAdminAsync()
    {
        var organizationId = await factory.CreateOrganizationAsync();
        var adminEmail = TestData.UniqueEmail("admin");
        var adminId = await factory.CreateUserAsync(organizationId, adminEmail, Role.Admin);
        return (organizationId, adminId, await factory.CreateClientForAsync(adminEmail));
    }

    private static object NewUserBody(string email, string password = TestData.Password, params object[] roles) =>
        new { email, displayName = "New Person", password, roles = roles.Length > 0 ? roles : [new { role = "Buyer" }] };

    [Fact]
    public async Task Admin_creates_a_user_who_can_then_log_in()
    {
        var (_, _, admin) = await NewOrganizationWithAdminAsync();
        var email = TestData.UniqueEmail("new");

        var response = await admin.PostAsJsonAsync("/api/v1/users", NewUserBody(email));

        response.StatusCode.Should().Be(HttpStatusCode.Created);
        response.Headers.Location.Should().NotBeNull();
        var user = await (await admin.GetAsync(response.Headers.Location)).ShouldBeOkAsync<UserDto>();
        user.Email.Should().Be(email);
        user.Roles.Should().ContainSingle(r => r.Role == "Buyer");
        (await factory.CreateClient().LoginAsync(email)).AccessToken.Should().NotBeEmpty();
    }

    [Fact]
    public async Task Email_must_be_unique_across_organizations()
    {
        var otherOrganization = await factory.CreateOrganizationAsync("Other");
        var email = TestData.UniqueEmail("taken");
        await factory.CreateUserAsync(otherOrganization, email, Role.Buyer);
        var (_, _, admin) = await NewOrganizationWithAdminAsync();

        var response = await admin.PostAsJsonAsync("/api/v1/users", NewUserBody(email.ToUpperInvariant()));

        await response.ShouldBeProblemAsync(HttpStatusCode.Conflict, "user.email_taken");
    }

    [Fact]
    public async Task Weak_password_is_rejected_and_nothing_is_saved()
    {
        var (_, _, admin) = await NewOrganizationWithAdminAsync();
        var email = TestData.UniqueEmail("weak");

        var response = await admin.PostAsJsonAsync("/api/v1/users", NewUserBody(email, password: "alllowercase"));
        var list = await (await admin.GetAsync($"/api/v1/users?search={email}")).ShouldBeOkAsync<PagedList<UserDto>>();

        await response.ShouldBeProblemAsync(HttpStatusCode.BadRequest, "user.password_rejected");
        list.Total.Should().Be(0);
    }

    [Fact]
    public async Task Unknown_role_name_is_a_validation_error()
    {
        var (_, _, admin) = await NewOrganizationWithAdminAsync();

        var response = await admin.PostAsJsonAsync("/api/v1/users", NewUserBody(TestData.UniqueEmail(), TestData.Password, new { role = "Wizard" }));

        await response.ShouldBeProblemAsync(HttpStatusCode.BadRequest, "request.validation_failed");
    }

    [Fact]
    public async Task Scoped_role_needs_a_cost_center_of_this_organization()
    {
        var (_, _, admin) = await NewOrganizationWithAdminAsync();
        var foreignOrganization = await factory.CreateOrganizationAsync("Foreign");
        var foreignCostCenter = await factory.CreateCostCenterAsync(foreignOrganization, "FX-001");

        var response = await admin.PostAsJsonAsync("/api/v1/users",
            NewUserBody(TestData.UniqueEmail(), TestData.Password, new { role = "Approver", costCenterId = foreignCostCenter }));

        await response.ShouldBeProblemAsync(HttpStatusCode.NotFound, "cost_center.not_found");
    }

    [Fact]
    public async Task Non_admin_cannot_manage_users()
    {
        var organizationId = await factory.CreateOrganizationAsync();
        var buyerEmail = TestData.UniqueEmail("buyer");
        await factory.CreateUserAsync(organizationId, buyerEmail, Role.Buyer);
        var buyer = await factory.CreateClientForAsync(buyerEmail);

        var response = await buyer.GetAsync("/api/v1/users");

        await response.ShouldBeProblemAsync(HttpStatusCode.Forbidden, "auth.forbidden");
    }

    [Fact]
    public async Task Replace_roles_works_but_an_admin_cannot_drop_their_own_admin_role()
    {
        var (organizationId, adminId, admin) = await NewOrganizationWithAdminAsync();
        var costCenter = await factory.CreateCostCenterAsync(organizationId, "IT-001");
        var otherId = await factory.CreateUserAsync(organizationId, TestData.UniqueEmail(), Role.Buyer);

        var replaceOther = await admin.PutAsJsonAsync($"/api/v1/users/{otherId}/roles",
            new { roles = new[] { new { role = "Approver", costCenterId = (Guid?)costCenter } } });
        var dropOwnAdmin = await admin.PutAsJsonAsync($"/api/v1/users/{adminId}/roles",
            new { roles = new[] { new { role = "Buyer" } } });

        replaceOther.StatusCode.Should().Be(HttpStatusCode.NoContent);
        var other = await (await admin.GetAsync($"/api/v1/users/{otherId}")).ShouldBeOkAsync<UserDto>();
        other.Roles.Should().ContainSingle().Which.Should().Be(new RoleAssignmentDto("Approver", costCenter));
        await dropOwnAdmin.ShouldBeProblemAsync(HttpStatusCode.UnprocessableEntity, "user.cannot_remove_own_admin");
    }

    [Fact]
    public async Task Deactivating_a_user_ends_their_sessions()
    {
        var (organizationId, adminId, admin) = await NewOrganizationWithAdminAsync();
        var email = TestData.UniqueEmail("leaver");
        var userId = await factory.CreateUserAsync(organizationId, email, Role.Buyer);
        var tokens = await factory.CreateClient().LoginAsync(email);

        var deactivate = await admin.PostAsync($"/api/v1/users/{userId}/deactivate", null);
        var refresh = await factory.CreateClient().PostAsJsonAsync("/api/v1/auth/refresh", new { refreshToken = tokens.RefreshToken });
        var login = await factory.CreateClient().PostAsJsonAsync("/api/v1/auth/login", new { email, password = TestData.Password });
        var self = await admin.PostAsync($"/api/v1/users/{adminId}/deactivate", null);

        deactivate.StatusCode.Should().Be(HttpStatusCode.NoContent);
        await refresh.ShouldBeProblemAsync(HttpStatusCode.Unauthorized, "auth.refresh_token_invalid");
        await login.ShouldBeProblemAsync(HttpStatusCode.Unauthorized, "auth.invalid_credentials");
        await self.ShouldBeProblemAsync(HttpStatusCode.UnprocessableEntity, "user.cannot_deactivate_self");
    }

    [Fact]
    public async Task Paging_is_validated()
    {
        var (_, _, admin) = await NewOrganizationWithAdminAsync();

        var response = await admin.GetAsync("/api/v1/users?page=0&pageSize=500");

        await response.ShouldBeProblemAsync(HttpStatusCode.BadRequest, "request.validation_failed");
    }

    [Fact]
    public async Task Cost_centers_are_created_by_admins_and_codes_are_unique()
    {
        var (_, _, admin) = await NewOrganizationWithAdminAsync();

        var created = await admin.PostAsJsonAsync("/api/v1/cost-centers", new { code = "fin-001", name = "Finance" });
        var duplicate = await admin.PostAsJsonAsync("/api/v1/cost-centers", new { code = "FIN-001", name = "Finance again" });
        var list = await (await admin.GetAsync("/api/v1/cost-centers"))
            .ShouldBeOkAsync<List<Mawrid.Application.Identity.CostCenters.CostCenterDto>>();

        created.StatusCode.Should().Be(HttpStatusCode.Created);
        await duplicate.ShouldBeProblemAsync(HttpStatusCode.Conflict, "cost_center.code_taken");
        list.Should().ContainSingle(c => c.Code == "FIN-001");
    }
}
