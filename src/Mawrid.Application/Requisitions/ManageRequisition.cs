using FluentValidation;
using Mawrid.Application.Common.Abstractions;
using Mawrid.Application.Common.Messaging;
using Mawrid.Application.Common.Persistence;
using Mawrid.Application.Common.Security;
using Mawrid.Application.Contracts.Approvals;
using Mawrid.Application.Contracts.Identity;
using Mawrid.Domain.Common;
using Mawrid.Domain.Requisitions;
using Microsoft.EntityFrameworkCore;

namespace Mawrid.Application.Requisitions;

public sealed record CreateRequisitionCommand(
    Guid CostCenterId, string Title, string Justification, DateOnly NeededBy, IReadOnlyList<RequisitionLineDto> Lines) : ICommand<Guid>;

public sealed record UpdateRequisitionCommand(
    Guid RequisitionId, Guid CostCenterId, string Title, string Justification, DateOnly NeededBy, IReadOnlyList<RequisitionLineDto> Lines) : ICommand;

internal sealed class RequisitionLineDtoValidator : AbstractValidator<RequisitionLineDto>
{
    public RequisitionLineDtoValidator()
    {
        RuleFor(x => x.Description).NotEmpty().MaximumLength(RequisitionLine.DescriptionMaxLength);
        RuleFor(x => x.Unit).NotEmpty().MaximumLength(RequisitionLine.UnitMaxLength);
        RuleFor(x => x.Quantity).GreaterThan(0);
        RuleFor(x => x.EstimatedUnitPrice).GreaterThanOrEqualTo(0);
    }
}

internal sealed class CreateRequisitionCommandValidator : AbstractValidator<CreateRequisitionCommand>
{
    public CreateRequisitionCommandValidator()
    {
        RuleFor(x => x.Title).NotEmpty().MaximumLength(PurchaseRequisition.TitleMaxLength);
        RuleFor(x => x.Justification).NotEmpty().MaximumLength(PurchaseRequisition.JustificationMaxLength);
        RuleFor(x => x.Lines).NotEmpty();
        RuleForEach(x => x.Lines).SetValidator(new RequisitionLineDtoValidator());
    }
}

internal sealed class UpdateRequisitionCommandValidator : AbstractValidator<UpdateRequisitionCommand>
{
    public UpdateRequisitionCommandValidator()
    {
        RuleFor(x => x.Title).NotEmpty().MaximumLength(PurchaseRequisition.TitleMaxLength);
        RuleFor(x => x.Justification).NotEmpty().MaximumLength(PurchaseRequisition.JustificationMaxLength);
        RuleFor(x => x.Lines).NotEmpty();
        RuleForEach(x => x.Lines).SetValidator(new RequisitionLineDtoValidator());
    }
}

internal sealed class CreateRequisitionCommandHandler(
    IAppDbContext db, ICurrentUser currentUser, IOrganizationDirectory directory, IDocumentNumbers numbers, IClock clock)
    : ICommandHandler<CreateRequisitionCommand, Guid>
{
    public async Task<Result<Guid>> Handle(CreateRequisitionCommand command, CancellationToken cancellationToken)
    {
        if (!RequisitionAccess.CanRequestFor(currentUser, command.CostCenterId))
        {
            return RequisitionErrors.NotRequesterOfCostCenter;
        }

        if (!await directory.IsActiveCostCenterAsync(command.CostCenterId, cancellationToken))
        {
            return RequisitionErrors.CostCenterNotFound;
        }

        var details = RequisitionMapping.ToDetails(command.Title, command.Justification, command.NeededBy, command.Lines);

        // Check the details before taking a number, so invalid input does not use one up.
        var check = PurchaseRequisition.CreateDraft(currentUser.OrganizationId!.Value, "PR-CHECK", currentUser.UserId!.Value,
            command.CostCenterId, details, clock.Today);
        if (check.IsFailure)
        {
            return check.Error!;
        }

        var number = await numbers.NextAsync("PR", cancellationToken);
        var requisition = PurchaseRequisition.CreateDraft(currentUser.OrganizationId!.Value, number, currentUser.UserId!.Value,
            command.CostCenterId, details, clock.Today).Value;

        db.Requisitions.Add(requisition);
        await db.SaveChangesAsync(cancellationToken);
        return requisition.Id;
    }
}

internal sealed class UpdateRequisitionCommandHandler(IAppDbContext db, ICurrentUser currentUser, IOrganizationDirectory directory, IClock clock)
    : ICommandHandler<UpdateRequisitionCommand>
{
    public async Task<Result> Handle(UpdateRequisitionCommand command, CancellationToken cancellationToken)
    {
        var requisition = await db.Requisitions.SingleOrDefaultAsync(r => r.Id == command.RequisitionId, cancellationToken);
        if (requisition is null || requisition.RequesterId != currentUser.UserId)
        {
            return requisition is null ? RequisitionErrors.NotFound : RequisitionErrors.NotOwner;
        }

        if (!RequisitionAccess.CanRequestFor(currentUser, command.CostCenterId))
        {
            return RequisitionErrors.NotRequesterOfCostCenter;
        }

        if (!await directory.IsActiveCostCenterAsync(command.CostCenterId, cancellationToken))
        {
            return RequisitionErrors.CostCenterNotFound;
        }

        var result = requisition.UpdateDraft(command.CostCenterId,
            RequisitionMapping.ToDetails(command.Title, command.Justification, command.NeededBy, command.Lines), clock.Today);
        if (result.IsSuccess)
        {
            await db.SaveChangesAsync(cancellationToken);
        }

        return result;
    }
}

/// <summary>Sends a draft for approval. The approval request and the requisition are saved together.</summary>
public sealed record SubmitRequisitionCommand(Guid RequisitionId) : ICommand;

internal sealed class SubmitRequisitionCommandHandler(IAppDbContext db, ICurrentUser currentUser, IApprovalService approvals)
    : ICommandHandler<SubmitRequisitionCommand>
{
    public async Task<Result> Handle(SubmitRequisitionCommand command, CancellationToken cancellationToken)
    {
        var requisition = await db.Requisitions.SingleOrDefaultAsync(r => r.Id == command.RequisitionId, cancellationToken);
        if (requisition is null || requisition.RequesterId != currentUser.UserId)
        {
            return requisition is null ? RequisitionErrors.NotFound : RequisitionErrors.NotOwner;
        }

        if (requisition.Status != RequisitionStatus.Draft)
        {
            return RequisitionErrors.NotDraft;
        }

        var approval = await approvals.StartAsync(new ApprovalStart(
            ApprovalDocumentTypes.PurchaseRequisition,
            requisition.Id,
            $"{requisition.Number} · {requisition.Title}",
            requisition.EstimatedTotalWithVat,
            requisition.CostCenterId,
            requisition.RequesterId), cancellationToken);
        if (approval.IsFailure)
        {
            return approval.Error!;
        }

        var result = requisition.Submit(approval.Value);
        if (result.IsSuccess)
        {
            await db.SaveChangesAsync(cancellationToken);
        }

        return result;
    }
}

public sealed record CancelRequisitionCommand(Guid RequisitionId) : ICommand;

internal sealed class CancelRequisitionCommandHandler(IAppDbContext db, ICurrentUser currentUser, IApprovalService approvals)
    : ICommandHandler<CancelRequisitionCommand>
{
    public async Task<Result> Handle(CancelRequisitionCommand command, CancellationToken cancellationToken)
    {
        var requisition = await db.Requisitions.SingleOrDefaultAsync(r => r.Id == command.RequisitionId, cancellationToken);
        if (requisition is null)
        {
            return RequisitionErrors.NotFound;
        }

        if (requisition.RequesterId != currentUser.UserId && !currentUser.HasRole(Role.Admin))
        {
            return RequisitionErrors.NotOwner;
        }

        var wasPending = requisition.Status == RequisitionStatus.PendingApproval;
        var result = requisition.Cancel();
        if (result.IsFailure)
        {
            return result;
        }

        if (wasPending && requisition.ApprovalRequestId is { } approvalId)
        {
            await approvals.CancelAsync(approvalId, cancellationToken);
        }

        await db.SaveChangesAsync(cancellationToken);
        return Result.Success();
    }
}

/// <summary>Procurement chooses how to buy an approved requisition.</summary>
public sealed record ChooseSourcingRouteCommand(Guid RequisitionId, string Route) : ICommand;

internal sealed class ChooseSourcingRouteCommandValidator : AbstractValidator<ChooseSourcingRouteCommand>
{
    public ChooseSourcingRouteCommandValidator() =>
        RuleFor(x => x.Route).Must(r => Enum.TryParse<SourcingRoute>(r, ignoreCase: true, out _))
            .WithMessage($"Route must be one of: {string.Join(", ", Enum.GetNames<SourcingRoute>())}.");
}

internal sealed class ChooseSourcingRouteCommandHandler(IAppDbContext db, IOrganizationDirectory directory)
    : ICommandHandler<ChooseSourcingRouteCommand>
{
    public async Task<Result> Handle(ChooseSourcingRouteCommand command, CancellationToken cancellationToken)
    {
        var requisition = await db.Requisitions.SingleOrDefaultAsync(r => r.Id == command.RequisitionId, cancellationToken);
        if (requisition is null)
        {
            return RequisitionErrors.NotFound;
        }

        var settings = await directory.GetSettingsAsync(cancellationToken);
        var result = requisition.ChooseSourcingRoute(Enum.Parse<SourcingRoute>(command.Route, ignoreCase: true), settings.DirectPurchaseLimit);
        if (result.IsSuccess)
        {
            await db.SaveChangesAsync(cancellationToken);
        }

        return result;
    }
}

/// <summary>Called by the Approvals module when a requisition's approval finishes.</summary>
internal sealed class RequisitionApprovalHandler(IAppDbContext db) : IApprovalDocumentHandler
{
    public string DocumentType => ApprovalDocumentTypes.PurchaseRequisition;

    public async Task<Result> OnApprovedAsync(Guid documentId, CancellationToken cancellationToken)
    {
        var requisition = await db.Requisitions.SingleAsync(r => r.Id == documentId, cancellationToken);
        return requisition.MarkApproved();
    }

    public async Task<Result> OnRejectedAsync(Guid documentId, string reason, CancellationToken cancellationToken)
    {
        var requisition = await db.Requisitions.SingleAsync(r => r.Id == documentId, cancellationToken);
        return requisition.MarkRejected(reason);
    }
}
