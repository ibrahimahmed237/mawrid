using Mawrid.Api.Common;
using Mawrid.Api.Common.Security;
using Mawrid.Application.Suppliers;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Mawrid.Api.Suppliers;

/// <summary>Endpoints for supplier users, about their own company.</summary>
[Route("api/v1/supplier")]
[Authorize(Policy = Policies.SupplierUser)]
public sealed class SupplierPortalController : ApiControllerBase
{
    [HttpGet("profile")]
    [ProducesResponseType<SupplierDto>(StatusCodes.Status200OK)]
    public async Task<IActionResult> Profile(CancellationToken cancellationToken) =>
        OkOrProblem(await Sender.Send(new GetMySupplierQuery(), cancellationToken));

    /// <summary>Attaches an uploaded file (see POST /api/v1/files) as a company document.</summary>
    [HttpPost("documents")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public async Task<IActionResult> AddDocument(AddSupplierDocumentCommand command, CancellationToken cancellationToken) =>
        NoContentOrProblem(await Sender.Send(command, cancellationToken));

    [HttpPost("bank-accounts")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public async Task<IActionResult> RequestBankAccount(RequestBankAccountCommand command, CancellationToken cancellationToken) =>
        NoContentOrProblem(await Sender.Send(command, cancellationToken));

    [HttpPost("submit")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public async Task<IActionResult> Submit(CancellationToken cancellationToken) =>
        NoContentOrProblem(await Sender.Send(new SubmitSupplierForReviewCommand(), cancellationToken));
}
