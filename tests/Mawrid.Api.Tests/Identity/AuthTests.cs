using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using Mawrid.Api.Tests.Infrastructure;
using Mawrid.Application.Identity.Auth;
using Mawrid.Application.Identity.Users;
using Mawrid.Domain.Identity;

namespace Mawrid.Api.Tests.Identity;

[Collection(ApiCollection.Name)]
public class AuthTests(MawridApiFactory factory)
{
    private async Task<string> NewBuyerAsync()
    {
        var organizationId = await factory.CreateOrganizationAsync();
        var email = TestData.UniqueEmail("buyer");
        await factory.CreateUserAsync(organizationId, email, Role.Buyer);
        return email;
    }

    [Fact]
    public async Task Login_returns_tokens_that_open_protected_endpoints()
    {
        var email = await NewBuyerAsync();
        var client = factory.CreateClient();

        var tokens = await client.LoginAsync(email);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", tokens.AccessToken);
        var me = await (await client.GetAsync("/api/v1/me")).ShouldBeOkAsync<UserDto>();

        tokens.TokenType.Should().Be("Bearer");
        tokens.AccessTokenExpiresAtUtc.Should().BeCloseTo(DateTimeOffset.UtcNow.AddMinutes(15), TimeSpan.FromMinutes(1));
        me.Email.Should().Be(email);
        me.Roles.Should().ContainSingle(r => r.Role == "Buyer");
    }

    [Fact]
    public async Task Wrong_password_and_unknown_email_get_the_same_answer()
    {
        var email = await NewBuyerAsync();
        var client = factory.CreateClient();

        var wrongPassword = await client.PostAsJsonAsync("/api/v1/auth/login", new { email, password = "Wrong-Passw0rd-1" });
        var unknownEmail = await client.PostAsJsonAsync("/api/v1/auth/login", new { email = TestData.UniqueEmail(), password = "Wrong-Passw0rd-1" });

        await wrongPassword.ShouldBeProblemAsync(HttpStatusCode.Unauthorized, "auth.invalid_credentials");
        await unknownEmail.ShouldBeProblemAsync(HttpStatusCode.Unauthorized, "auth.invalid_credentials");
    }

    [Fact]
    public async Task Five_wrong_passwords_lock_the_account()
    {
        var email = await NewBuyerAsync();
        var client = factory.CreateClient();

        for (var attempt = 1; attempt <= 5; attempt++)
        {
            await client.PostAsJsonAsync("/api/v1/auth/login", new { email, password = "Wrong-Passw0rd-1" });
        }

        var withRightPassword = await client.PostAsJsonAsync("/api/v1/auth/login", new { email, password = TestData.Password });

        await withRightPassword.ShouldBeProblemAsync(HttpStatusCode.Unauthorized, "auth.locked_out");
    }

    [Fact]
    public async Task Refresh_gives_a_new_pair_and_the_old_refresh_token_stops_working()
    {
        var email = await NewBuyerAsync();
        var client = factory.CreateClient();
        var first = await client.LoginAsync(email);

        var second = await (await client.PostAsJsonAsync("/api/v1/auth/refresh", new { refreshToken = first.RefreshToken }))
            .ShouldBeOkAsync<TokenPairDto>();

        second.RefreshToken.Should().NotBe(first.RefreshToken);
        second.AccessToken.Should().NotBeNullOrEmpty();
    }

    [Fact]
    public async Task Reusing_a_refresh_token_revokes_the_whole_family()
    {
        var email = await NewBuyerAsync();
        var client = factory.CreateClient();
        var first = await client.LoginAsync(email);
        var second = await (await client.PostAsJsonAsync("/api/v1/auth/refresh", new { refreshToken = first.RefreshToken }))
            .ShouldBeOkAsync<TokenPairDto>();

        // An attacker replays the first (already used) token.
        var replay = await client.PostAsJsonAsync("/api/v1/auth/refresh", new { refreshToken = first.RefreshToken });
        // The real user's newer token is now dead too.
        var afterReplay = await client.PostAsJsonAsync("/api/v1/auth/refresh", new { refreshToken = second.RefreshToken });

        await replay.ShouldBeProblemAsync(HttpStatusCode.Unauthorized, "auth.refresh_token_reused");
        await afterReplay.ShouldBeProblemAsync(HttpStatusCode.Unauthorized, "auth.refresh_token_invalid");
    }

    [Fact]
    public async Task Logout_ends_the_session()
    {
        var email = await NewBuyerAsync();
        var client = factory.CreateClient();
        var tokens = await client.LoginAsync(email);

        var logout = await client.PostAsJsonAsync("/api/v1/auth/logout", new { refreshToken = tokens.RefreshToken });
        var refresh = await client.PostAsJsonAsync("/api/v1/auth/refresh", new { refreshToken = tokens.RefreshToken });

        logout.StatusCode.Should().Be(HttpStatusCode.NoContent);
        await refresh.ShouldBeProblemAsync(HttpStatusCode.Unauthorized, "auth.refresh_token_invalid");
    }

    [Fact]
    public async Task Unknown_refresh_token_is_rejected()
    {
        var response = await factory.CreateClient().PostAsJsonAsync("/api/v1/auth/refresh", new { refreshToken = "not-a-real-token" });

        await response.ShouldBeProblemAsync(HttpStatusCode.Unauthorized, "auth.refresh_token_invalid");
    }

    [Theory]
    [InlineData(null)]
    [InlineData("garbage.token.value")]
    public async Task Protected_endpoint_needs_a_valid_token(string? token)
    {
        var client = factory.CreateClient();
        if (token is not null)
        {
            client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        }

        var response = await client.GetAsync("/api/v1/me");

        await response.ShouldBeProblemAsync(HttpStatusCode.Unauthorized, "auth.unauthenticated");
    }

    [Fact]
    public async Task Malformed_json_returns_a_validation_problem()
    {
        using var content = new StringContent("{ not json", System.Text.Encoding.UTF8, "application/json");

        var response = await factory.CreateClient().PostAsync("/api/v1/auth/login", content);

        await response.ShouldBeProblemAsync(HttpStatusCode.BadRequest, "request.validation_failed");
    }

    [Fact]
    public async Task Empty_login_fields_return_field_errors()
    {
        var response = await factory.CreateClient().PostAsJsonAsync("/api/v1/auth/login", new { email = "", password = "" });

        await response.ShouldBeProblemAsync(HttpStatusCode.BadRequest, "request.validation_failed");
    }
}
