using Mawrid.Api.Common;
using Mawrid.Api.Common.Errors;
using Mawrid.Api.Common.Security;
using Mawrid.Application.Common.Paging;
using Mawrid.Application.Integrations;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Mawrid.Api.Integrations;

/// <summary>Webhook subscriptions for the ERP. The secret is returned only when it is created or rotated.</summary>
[Route("api/v1/webhook-subscriptions")]
[Authorize(Policy = Policies.Admin)]
public sealed class WebhookSubscriptionsController : ApiControllerBase
{
    [HttpGet]
    [ProducesResponseType<IReadOnlyList<WebhookSubscriptionDto>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> List(CancellationToken cancellationToken) =>
        OkOrProblem(await Sender.Send(new GetWebhookSubscriptionsQuery(), cancellationToken));

    [HttpPost]
    [ProducesResponseType<WebhookSecretDto>(StatusCodes.Status201Created)]
    public async Task<IActionResult> Create(CreateWebhookSubscriptionCommand command, CancellationToken cancellationToken)
    {
        var result = await Sender.Send(command, cancellationToken);
        return result.IsSuccess ? Created($"/api/v1/webhook-subscriptions/{result.Value.Id}", result.Value) : result.Error!.ToActionResult();
    }

    public sealed record UpdateRequest(string Name, string Url, IReadOnlyList<string> EventTypes, bool IsActive);

    [HttpPut("{id:guid}")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public async Task<IActionResult> Update(Guid id, UpdateRequest request, CancellationToken cancellationToken) =>
        NoContentOrProblem(await Sender.Send(new UpdateWebhookSubscriptionCommand(id, request.Name, request.Url, request.EventTypes, request.IsActive),
            cancellationToken));

    [HttpPost("{id:guid}/rotate-secret")]
    [ProducesResponseType<WebhookSecretDto>(StatusCodes.Status200OK)]
    public async Task<IActionResult> RotateSecret(Guid id, CancellationToken cancellationToken) =>
        OkOrProblem(await Sender.Send(new RotateWebhookSecretCommand(id), cancellationToken));
}

/// <summary>The delivery log: what was sent, the answer, and replay.</summary>
[Route("api/v1/webhook-deliveries")]
[Authorize(Policy = Policies.Admin)]
public sealed class WebhookDeliveriesController : ApiControllerBase
{
    [HttpGet]
    [ProducesResponseType<PagedList<WebhookDeliveryDto>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> List([FromQuery] int page = 1, [FromQuery] int pageSize = 20, [FromQuery] string? status = null,
        CancellationToken cancellationToken = default) =>
        OkOrProblem(await Sender.Send(new GetWebhookDeliveriesQuery(new PageRequest(page, pageSize), status), cancellationToken));

    [HttpPost("{id:guid}/replay")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public async Task<IActionResult> Replay(Guid id, CancellationToken cancellationToken) =>
        NoContentOrProblem(await Sender.Send(new ReplayWebhookDeliveryCommand(id), cancellationToken));
}
