using FluentValidation;
using Mawrid.Application.Common.Abstractions;
using Mawrid.Application.Common.Messaging;
using Mawrid.Application.Common.Persistence;
using Mawrid.Application.Common.Security;
using Mawrid.Application.Contracts.Identity;
using Mawrid.Application.Contracts.Requisitions;
using Mawrid.Application.Contracts.Suppliers;
using Mawrid.Domain.Common;
using Mawrid.Domain.Sourcing;
using Microsoft.EntityFrameworkCore;

namespace Mawrid.Application.Sourcing;

/// <summary>Creates a draft RFQ from approved requisitions (route RFQ). Their lines become the RFQ items.</summary>
public sealed record CreateRfqCommand(string Title, string? Description, IReadOnlyList<Guid> RequisitionIds) : ICommand<Guid>;

internal sealed class CreateRfqCommandValidator : AbstractValidator<CreateRfqCommand>
{
    public CreateRfqCommandValidator()
    {
        RuleFor(x => x.Title).NotEmpty().MaximumLength(Rfq.TitleMaxLength);
        RuleFor(x => x.Description).MaximumLength(Rfq.DescriptionMaxLength);
        RuleFor(x => x.RequisitionIds).NotEmpty();
    }
}

internal sealed class CreateRfqCommandHandler(
    IAppDbContext db, ICurrentUser currentUser, IRequisitionSourcing requisitions, IDocumentNumbers numbers) : ICommandHandler<CreateRfqCommand, Guid>
{
    public async Task<Result<Guid>> Handle(CreateRfqCommand command, CancellationToken cancellationToken)
    {
        var sourcing = await requisitions.GetForRfqAsync(command.RequisitionIds, cancellationToken);
        if (sourcing.IsFailure)
        {
            return sourcing.Error!;
        }

        var items = sourcing.Value.Lines
            .Select(l => new RfqItemInput(l.Description, l.Quantity, l.Unit, l.RequisitionId, l.LineNumber))
            .ToList();

        var number = await numbers.NextAsync("RFQ", cancellationToken);
        var created = Rfq.CreateDraft(currentUser.OrganizationId!.Value, number, currentUser.UserId!.Value, sourcing.Value.CostCenterId,
            command.Title, command.Description, items);
        if (created.IsFailure)
        {
            return created.Error!;
        }

        db.Rfqs.Add(created.Value);
        await db.SaveChangesAsync(cancellationToken);
        return created.Value.Id;
    }
}

public sealed record SetRfqDeadlineCommand(Guid RfqId, DateTimeOffset Deadline) : ICommand;

internal sealed class SetRfqDeadlineCommandHandler(IAppDbContext db) : ICommandHandler<SetRfqDeadlineCommand>
{
    public async Task<Result> Handle(SetRfqDeadlineCommand command, CancellationToken cancellationToken)
    {
        var rfq = await db.Rfqs.SingleOrDefaultAsync(r => r.Id == command.RfqId, cancellationToken);
        if (rfq is null)
        {
            return SourcingErrors.NotFound;
        }

        var result = rfq.SetDeadline(command.Deadline.ToUniversalTime());
        return await SaveIfSuccess(db, result, cancellationToken);
    }

    internal static async Task<Result> SaveIfSuccess(IAppDbContext db, Result result, CancellationToken cancellationToken)
    {
        if (result.IsSuccess)
        {
            await db.SaveChangesAsync(cancellationToken);
        }

        return result;
    }
}

public sealed record InviteSuppliersCommand(Guid RfqId, IReadOnlyList<Guid> SupplierIds) : ICommand;

internal sealed class InviteSuppliersCommandValidator : AbstractValidator<InviteSuppliersCommand>
{
    public InviteSuppliersCommandValidator() => RuleFor(x => x.SupplierIds).NotEmpty().Must(ids => ids.Count <= 50);
}

internal sealed class InviteSuppliersCommandHandler(IAppDbContext db, ISupplierDirectory suppliers, IClock clock) : ICommandHandler<InviteSuppliersCommand>
{
    public async Task<Result> Handle(InviteSuppliersCommand command, CancellationToken cancellationToken)
    {
        var rfq = await db.Rfqs.SingleOrDefaultAsync(r => r.Id == command.RfqId, cancellationToken);
        if (rfq is null)
        {
            return SourcingErrors.NotFound;
        }

        var ids = command.SupplierIds.Distinct().ToList();
        var found = await suppliers.FindManyAsync(ids, cancellationToken);
        if (found.Count != ids.Count || found.Any(s => !s.IsAwardable))
        {
            return SourcingErrors.SupplierNotAwardable;
        }

        return await SetRfqDeadlineCommandHandler.SaveIfSuccess(db, rfq.Invite(ids, clock.UtcNow), cancellationToken);
    }
}

public sealed record RemoveInvitationCommand(Guid RfqId, Guid SupplierId) : ICommand;

internal sealed class RemoveInvitationCommandHandler(IAppDbContext db) : ICommandHandler<RemoveInvitationCommand>
{
    public async Task<Result> Handle(RemoveInvitationCommand command, CancellationToken cancellationToken)
    {
        var rfq = await db.Rfqs.SingleOrDefaultAsync(r => r.Id == command.RfqId, cancellationToken);
        return rfq is null
            ? SourcingErrors.NotFound
            : await SetRfqDeadlineCommandHandler.SaveIfSuccess(db, rfq.RemoveInvitation(command.SupplierId), cancellationToken);
    }
}

public sealed record PublishRfqCommand(Guid RfqId, string? SingleSourceReason) : ICommand;

internal sealed class PublishRfqCommandHandler(IAppDbContext db, ISupplierDirectory suppliers, IOrganizationDirectory organization, IClock clock)
    : ICommandHandler<PublishRfqCommand>
{
    public async Task<Result> Handle(PublishRfqCommand command, CancellationToken cancellationToken)
    {
        var rfq = await db.Rfqs.SingleOrDefaultAsync(r => r.Id == command.RfqId, cancellationToken);
        if (rfq is null)
        {
            return SourcingErrors.NotFound;
        }

        // Suppliers can lose awardability between invitation and publishing (for example an expired certificate).
        var invited = await suppliers.FindManyAsync(rfq.Invitations.Select(i => i.SupplierId).ToList(), cancellationToken);
        if (invited.Any(s => !s.IsAwardable))
        {
            return SourcingErrors.SupplierNotAwardable;
        }

        var settings = await organization.GetSettingsAsync(cancellationToken);
        var result = rfq.Publish(clock.UtcNow, settings.MinimumRfqWindowDays, settings.MinimumSuppliersPerRfq, command.SingleSourceReason);
        return await SetRfqDeadlineCommandHandler.SaveIfSuccess(db, result, cancellationToken);
    }
}

/// <summary>Changes quantities and/or extends the deadline of a published RFQ.</summary>
public sealed record AmendRfqCommand(Guid RfqId, DateTimeOffset Deadline, IReadOnlyDictionary<int, decimal>? Quantities) : ICommand;

internal sealed class AmendRfqCommandHandler(IAppDbContext db, IOrganizationDirectory organization, IClock clock) : ICommandHandler<AmendRfqCommand>
{
    public async Task<Result> Handle(AmendRfqCommand command, CancellationToken cancellationToken)
    {
        var rfq = await db.Rfqs.SingleOrDefaultAsync(r => r.Id == command.RfqId, cancellationToken);
        if (rfq is null)
        {
            return SourcingErrors.NotFound;
        }

        var settings = await organization.GetSettingsAsync(cancellationToken);
        var result = rfq.Amend(clock.UtcNow, command.Deadline.ToUniversalTime(), command.Quantities ?? new Dictionary<int, decimal>(), settings.MinimumRfqWindowDays);
        return await SetRfqDeadlineCommandHandler.SaveIfSuccess(db, result, cancellationToken);
    }
}

public sealed record CancelRfqCommand(Guid RfqId, string Reason) : ICommand;

internal sealed class CancelRfqCommandHandler(IAppDbContext db) : ICommandHandler<CancelRfqCommand>
{
    public async Task<Result> Handle(CancelRfqCommand command, CancellationToken cancellationToken)
    {
        var rfq = await db.Rfqs.SingleOrDefaultAsync(r => r.Id == command.RfqId, cancellationToken);
        return rfq is null
            ? SourcingErrors.NotFound
            : await SetRfqDeadlineCommandHandler.SaveIfSuccess(db, rfq.Cancel(command.Reason), cancellationToken);
    }
}
