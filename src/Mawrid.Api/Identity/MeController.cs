using Mawrid.Api.Common;
using Mawrid.Application.Identity.Users;
using Microsoft.AspNetCore.Mvc;

namespace Mawrid.Api.Identity;

[Route("api/v1/me")]
public sealed class MeController : ApiControllerBase
{
    /// <summary>The logged-in user, with their roles.</summary>
    [HttpGet]
    [ProducesResponseType<UserDto>(StatusCodes.Status200OK)]
    public async Task<IActionResult> Get(CancellationToken cancellationToken) =>
        OkOrProblem(await Sender.Send(new GetMeQuery(), cancellationToken));
}
