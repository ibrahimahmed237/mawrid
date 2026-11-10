using Mawrid.Api.Common;
using Mawrid.Api.Common.Security;
using Mawrid.Application.Contracts.Identity;
using Mawrid.Application.Identity.Settings;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Mawrid.Api.Identity;

[Route("api/v1/organization/settings")]
[Authorize(Policy = Policies.InternalUser)]
public sealed class OrganizationSettingsController : ApiControllerBase
{
    [HttpGet]
    [ProducesResponseType<ProcurementSettingsInfo>(StatusCodes.Status200OK)]
    public async Task<IActionResult> Get(CancellationToken cancellationToken) =>
        OkOrProblem(await Sender.Send(new GetProcurementSettingsQuery(), cancellationToken));

    [HttpPut]
    [Authorize(Policy = Policies.Admin)]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public async Task<IActionResult> Update(UpdateProcurementSettingsCommand command, CancellationToken cancellationToken) =>
        NoContentOrProblem(await Sender.Send(command, cancellationToken));
}
