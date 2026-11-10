using FluentValidation;
using Mawrid.Application.Common.Messaging;
using Mawrid.Application.Common.Persistence;
using Mawrid.Application.Common.Security;
using Mawrid.Application.Contracts.Identity;
using Mawrid.Domain.Common;
using Microsoft.EntityFrameworkCore;
using DomainSettings = Mawrid.Domain.Identity.ProcurementSettings;

namespace Mawrid.Application.Identity.Settings;

public sealed record GetProcurementSettingsQuery : IQuery<ProcurementSettingsInfo>;

internal sealed class GetProcurementSettingsQueryHandler(IOrganizationDirectory directory)
    : IQueryHandler<GetProcurementSettingsQuery, ProcurementSettingsInfo>
{
    public async Task<Result<ProcurementSettingsInfo>> Handle(GetProcurementSettingsQuery query, CancellationToken cancellationToken) =>
        await directory.GetSettingsAsync(cancellationToken);
}

public sealed record UpdateProcurementSettingsCommand(
    decimal DirectPurchaseLimit,
    int MinimumSuppliersPerRfq,
    int MinimumRfqWindowDays,
    decimal PriceTolerancePercent,
    decimal PriceToleranceMaxAmount,
    decimal OverDeliveryPercent) : ICommand;

internal sealed class UpdateProcurementSettingsCommandHandler(IAppDbContext db, ICurrentUser currentUser)
    : ICommandHandler<UpdateProcurementSettingsCommand>
{
    public async Task<Result> Handle(UpdateProcurementSettingsCommand command, CancellationToken cancellationToken)
    {
        var organization = await db.Organizations.SingleAsync(o => o.Id == currentUser.OrganizationId, cancellationToken);
        var result = organization.UpdateSettings(new DomainSettings(
            command.DirectPurchaseLimit, command.MinimumSuppliersPerRfq, command.MinimumRfqWindowDays,
            command.PriceTolerancePercent, command.PriceToleranceMaxAmount, command.OverDeliveryPercent));

        if (result.IsSuccess)
        {
            await db.SaveChangesAsync(cancellationToken);
        }

        return result;
    }
}
