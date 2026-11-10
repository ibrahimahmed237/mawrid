using FluentValidation;
using Mawrid.Application.Common.Messaging;
using Mawrid.Application.Common.Persistence;
using Mawrid.Application.Common.Security;
using Mawrid.Domain.Common;
using Mawrid.Domain.Identity;
using Microsoft.EntityFrameworkCore;

namespace Mawrid.Application.Identity.CostCenters;

public sealed record CostCenterDto(Guid Id, string Code, string Name, bool IsActive);

public sealed record CreateCostCenterCommand(string Code, string Name) : ICommand<Guid>;

internal sealed class CreateCostCenterCommandValidator : AbstractValidator<CreateCostCenterCommand>
{
    public CreateCostCenterCommandValidator()
    {
        RuleFor(x => x.Code).NotEmpty().MaximumLength(CostCenter.CodeMaxLength);
        RuleFor(x => x.Name).NotEmpty().MaximumLength(CostCenter.NameMaxLength);
    }
}

internal sealed class CreateCostCenterCommandHandler(IAppDbContext db, ICurrentUser currentUser)
    : ICommandHandler<CreateCostCenterCommand, Guid>
{
    public async Task<Result<Guid>> Handle(CreateCostCenterCommand command, CancellationToken cancellationToken)
    {
        var organizationId = currentUser.OrganizationId
            ?? throw new InvalidOperationException("Creating a cost center needs an organization context.");

        var created = CostCenter.Create(organizationId, command.Code, command.Name);
        if (created.IsFailure)
        {
            return created.Error!;
        }

        var costCenter = created.Value;
        if (await db.CostCenters.AnyAsync(c => c.Code == costCenter.Code, cancellationToken))
        {
            return IdentityErrors.CostCenterCodeTaken;
        }

        db.CostCenters.Add(costCenter);
        await db.SaveChangesAsync(cancellationToken);
        return costCenter.Id;
    }
}

public sealed record GetCostCentersQuery(bool IncludeInactive = false) : IQuery<IReadOnlyList<CostCenterDto>>;

internal sealed class GetCostCentersQueryHandler(IAppDbContext db) : IQueryHandler<GetCostCentersQuery, IReadOnlyList<CostCenterDto>>
{
    public async Task<Result<IReadOnlyList<CostCenterDto>>> Handle(GetCostCentersQuery query, CancellationToken cancellationToken)
    {
        var costCenters = db.CostCenters.AsNoTracking();
        if (!query.IncludeInactive)
        {
            costCenters = costCenters.Where(c => c.IsActive);
        }

        var list = await costCenters
            .OrderBy(c => c.Code)
            .Select(c => new CostCenterDto(c.Id, c.Code, c.Name, c.IsActive))
            .ToListAsync(cancellationToken);
        return list;
    }
}
