using Mawrid.Api.Common;
using Mawrid.Api.Common.Errors;
using Mawrid.Api.Common.Security;
using Mawrid.Application.Identity.CostCenters;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Mawrid.Api.Identity;

[Route("api/v1/cost-centers")]
[Authorize(Policy = Policies.InternalUser)]
public sealed class CostCentersController : ApiControllerBase
{
    [HttpGet]
    [ProducesResponseType<IReadOnlyList<CostCenterDto>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> List([FromQuery] bool includeInactive = false, CancellationToken cancellationToken = default) =>
        OkOrProblem(await Sender.Send(new GetCostCentersQuery(includeInactive), cancellationToken));

    [HttpPost]
    [Authorize(Policy = Policies.Admin)]
    [ProducesResponseType<CreatedId>(StatusCodes.Status201Created)]
    public async Task<IActionResult> Create(CreateCostCenterCommand command, CancellationToken cancellationToken)
    {
        var result = await Sender.Send(command, cancellationToken);
        return result.IsSuccess
            ? Created($"/api/v1/cost-centers/{result.Value}", new CreatedId(result.Value))
            : result.Error!.ToActionResult();
    }
}
