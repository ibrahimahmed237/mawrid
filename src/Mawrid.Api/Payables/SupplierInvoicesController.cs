using Mawrid.Api.Common;
using Mawrid.Api.Common.Errors;
using Mawrid.Api.Common.Idempotency;
using Mawrid.Api.Common.Security;
using Mawrid.Application.Payables;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace Mawrid.Api.Payables;

/// <summary>Invoices from the supplier's side: send a UBL e-invoice and follow its status.</summary>
[Route("api/v1/supplier/invoices")]
[Authorize(Policy = Policies.SupplierUser)]
[EnableRateLimiting(RateLimits.SupplierPortal)]
public sealed class SupplierInvoicesController : ApiControllerBase
{
    [HttpGet]
    [ProducesResponseType<IReadOnlyList<SupplierInvoiceDto>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> List(CancellationToken cancellationToken) =>
        OkOrProblem(await Sender.Send(new GetMyInvoicesQuery(), cancellationToken));

    [HttpGet("{id:guid}")]
    [ProducesResponseType<SupplierInvoiceDto>(StatusCodes.Status200OK)]
    public async Task<IActionResult> Get(Guid id, CancellationToken cancellationToken) =>
        OkOrProblem(await Sender.Send(new GetMyInvoiceQuery(id), cancellationToken));

    /// <summary>Sends an invoice: upload the XML (and PDF) through /files first, then post their ids.</summary>
    [HttpPost]
    [Idempotent]
    [ProducesResponseType<CreatedId>(StatusCodes.Status201Created)]
    public async Task<IActionResult> Submit(SubmitInvoiceCommand command, CancellationToken cancellationToken)
    {
        var result = await Sender.Send(command, cancellationToken);
        return result.IsSuccess ? CreatedAtAction(nameof(Get), new { id = result.Value }, new CreatedId(result.Value)) : result.Error!.ToActionResult();
    }
}
