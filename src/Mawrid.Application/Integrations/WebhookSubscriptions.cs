using FluentValidation;
using Mawrid.Application.Common.Messaging;
using Mawrid.Application.Common.Persistence;
using Mawrid.Application.Common.Security;
using Mawrid.Domain.Common;
using Mawrid.Domain.Integrations;
using Microsoft.EntityFrameworkCore;

namespace Mawrid.Application.Integrations;

public sealed record WebhookSubscriptionDto(Guid Id, string Name, string Url, IReadOnlyList<string> EventTypes, bool IsActive);

/// <summary>Returned once, when the secret is created or rotated. Store it in the receiving system.</summary>
public sealed record WebhookSecretDto(Guid Id, string Secret);

/// <param name="Secret">Optional: a secret you choose (32+ characters). Otherwise Mawrid creates one.</param>
public sealed record CreateWebhookSubscriptionCommand(string Name, string Url, IReadOnlyList<string> EventTypes, string? Secret = null)
    : ICommand<WebhookSecretDto>;

internal sealed class CreateWebhookSubscriptionCommandValidator : AbstractValidator<CreateWebhookSubscriptionCommand>
{
    public CreateWebhookSubscriptionCommandValidator()
    {
        RuleFor(x => x.Name).NotEmpty().MaximumLength(WebhookSubscription.NameMaxLength);
        RuleFor(x => x.Url).NotEmpty().MaximumLength(WebhookSubscription.UrlMaxLength);
        RuleFor(x => x.EventTypes).NotEmpty();
    }
}

internal sealed class CreateWebhookSubscriptionCommandHandler(IAppDbContext db, ICurrentUser currentUser, IWebhookSecrets secrets, IWebhookSettings settings)
    : ICommandHandler<CreateWebhookSubscriptionCommand, WebhookSecretDto>
{
    public async Task<Result<WebhookSecretDto>> Handle(CreateWebhookSubscriptionCommand command, CancellationToken cancellationToken)
    {
        if (!WebhookUrls.TryParse(command.Url, settings, out var url))
        {
            return IntegrationErrors.UrlInvalid;
        }

        if (command.Secret is { Length: < 32 or > 200 })
        {
            return IntegrationErrors.SecretInvalid;
        }

        var secret = command.Secret ?? secrets.Generate();
        var created = WebhookSubscription.Create(currentUser.OrganizationId!.Value, command.Name, url, command.EventTypes, secrets.Protect(secret));
        if (created.IsFailure)
        {
            return created.Error!;
        }

        db.WebhookSubscriptions.Add(created.Value);
        await db.SaveChangesAsync(cancellationToken);
        return new WebhookSecretDto(created.Value.Id, secret);
    }
}

public sealed record UpdateWebhookSubscriptionCommand(Guid SubscriptionId, string Name, string Url, IReadOnlyList<string> EventTypes, bool IsActive) : ICommand;

internal sealed class UpdateWebhookSubscriptionCommandHandler(IAppDbContext db, IWebhookSettings settings) : ICommandHandler<UpdateWebhookSubscriptionCommand>
{
    public async Task<Result> Handle(UpdateWebhookSubscriptionCommand command, CancellationToken cancellationToken)
    {
        var subscription = await db.WebhookSubscriptions.SingleOrDefaultAsync(s => s.Id == command.SubscriptionId, cancellationToken);
        if (subscription is null)
        {
            return IntegrationErrors.SubscriptionNotFound;
        }

        if (!WebhookUrls.TryParse(command.Url, settings, out var url))
        {
            return IntegrationErrors.UrlInvalid;
        }

        var result = subscription.Update(command.Name, url, command.EventTypes, command.IsActive);
        if (result.IsSuccess)
        {
            await db.SaveChangesAsync(cancellationToken);
        }

        return result;
    }
}

public sealed record RotateWebhookSecretCommand(Guid SubscriptionId) : ICommand<WebhookSecretDto>;

internal sealed class RotateWebhookSecretCommandHandler(IAppDbContext db, IWebhookSecrets secrets) : ICommandHandler<RotateWebhookSecretCommand, WebhookSecretDto>
{
    public async Task<Result<WebhookSecretDto>> Handle(RotateWebhookSecretCommand command, CancellationToken cancellationToken)
    {
        var subscription = await db.WebhookSubscriptions.SingleOrDefaultAsync(s => s.Id == command.SubscriptionId, cancellationToken);
        if (subscription is null)
        {
            return IntegrationErrors.SubscriptionNotFound;
        }

        var secret = secrets.Generate();
        subscription.RotateSecret(secrets.Protect(secret));
        await db.SaveChangesAsync(cancellationToken);
        return new WebhookSecretDto(subscription.Id, secret);
    }
}

public sealed record GetWebhookSubscriptionsQuery : IQuery<IReadOnlyList<WebhookSubscriptionDto>>;

internal sealed class GetWebhookSubscriptionsQueryHandler(IAppDbContext db) : IQueryHandler<GetWebhookSubscriptionsQuery, IReadOnlyList<WebhookSubscriptionDto>>
{
    public async Task<Result<IReadOnlyList<WebhookSubscriptionDto>>> Handle(GetWebhookSubscriptionsQuery query, CancellationToken cancellationToken)
    {
        var subscriptions = await db.WebhookSubscriptions.AsNoTracking().OrderBy(s => s.Name).ToListAsync(cancellationToken);
        return subscriptions.Select(s => new WebhookSubscriptionDto(s.Id, s.Name, s.Url, s.EventTypes, s.IsActive)).ToList();
    }
}

internal static class WebhookUrls
{
    /// <summary>HTTPS only, unless the settings allow HTTP for local development.</summary>
    public static bool TryParse(string text, IWebhookSettings settings, out Uri url) =>
        Uri.TryCreate(text, UriKind.Absolute, out url!) && (url.Scheme == Uri.UriSchemeHttps || (settings.AllowHttp && url.Scheme == Uri.UriSchemeHttp));
}
