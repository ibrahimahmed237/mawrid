using Mawrid.Api.Common;
using Mawrid.Api.Common.Errors;
using Mawrid.Api.Common.Security;
using Mawrid.Application.Common.Paging;
using Mawrid.Application.Identity.Users;
using Mawrid.Domain.Common;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Mawrid.Api.Identity;

[Route("api/v1/users")]
[Authorize(Policy = Policies.Admin)]
public sealed class UsersController : ApiControllerBase
{
    [HttpGet]
    [ProducesResponseType<PagedList<UserDto>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> List(
        [FromQuery] int page = 1, [FromQuery] int pageSize = 20, [FromQuery] string? search = null, CancellationToken cancellationToken = default) =>
        OkOrProblem(await Sender.Send(new GetUsersQuery(new PageRequest(page, pageSize), search), cancellationToken));

    [HttpGet("{id:guid}")]
    [ProducesResponseType<UserDto>(StatusCodes.Status200OK)]
    public async Task<IActionResult> Get(Guid id, CancellationToken cancellationToken) =>
        OkOrProblem(await Sender.Send(new GetUserQuery(id), cancellationToken));

    [HttpPost]
    [ProducesResponseType<CreatedId>(StatusCodes.Status201Created)]
    public async Task<IActionResult> Create(CreateUserCommand command, CancellationToken cancellationToken)
    {
        var result = await Sender.Send(command, cancellationToken);
        return result.IsSuccess
            ? CreatedAtAction(nameof(Get), new { id = result.Value }, new CreatedId(result.Value))
            : result.Error!.ToActionResult();
    }

    public sealed record ReplaceRolesRequest(IReadOnlyList<RoleAssignmentDto> Roles);

    [HttpPut("{id:guid}/roles")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public async Task<IActionResult> ReplaceRoles(Guid id, ReplaceRolesRequest request, CancellationToken cancellationToken) =>
        NoContentOrProblem(await Sender.Send(new ReplaceUserRolesCommand(id, request.Roles), cancellationToken));

    [HttpPost("{id:guid}/deactivate")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public async Task<IActionResult> Deactivate(Guid id, CancellationToken cancellationToken) =>
        NoContentOrProblem(await Sender.Send(new DeactivateUserCommand(id), cancellationToken));
}
