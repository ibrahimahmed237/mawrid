using Mawrid.Api.Common;
using Mawrid.Application.Identity.Auth;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Mawrid.Api.Identity;

[Route("api/v1/auth")]
[AllowAnonymous]
public sealed class AuthController : ApiControllerBase
{
    /// <summary>Logs in with email and password. Returns an access token (15 minutes) and a refresh token.</summary>
    [HttpPost("login")]
    [ProducesResponseType<TokenPairDto>(StatusCodes.Status200OK)]
    public async Task<IActionResult> Login(LoginCommand command, CancellationToken cancellationToken) =>
        OkOrProblem(await Sender.Send(command, cancellationToken));

    /// <summary>Swaps a refresh token for a new token pair. Each refresh token works once.</summary>
    [HttpPost("refresh")]
    [ProducesResponseType<TokenPairDto>(StatusCodes.Status200OK)]
    public async Task<IActionResult> Refresh(RefreshTokensCommand command, CancellationToken cancellationToken) =>
        OkOrProblem(await Sender.Send(command, cancellationToken));

    /// <summary>Ends the session of this refresh token.</summary>
    [HttpPost("logout")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public async Task<IActionResult> Logout(LogoutCommand command, CancellationToken cancellationToken) =>
        NoContentOrProblem(await Sender.Send(command, cancellationToken));
}
