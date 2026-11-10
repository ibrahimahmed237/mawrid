using FluentValidation;
using Mawrid.Application.Common.Abstractions;
using Mawrid.Application.Common.Messaging;
using Mawrid.Application.Common.Persistence;
using Mawrid.Application.Common.Security;
using Mawrid.Domain.Common;
using Mawrid.Domain.Suppliers;
using Microsoft.EntityFrameworkCore;

namespace Mawrid.Application.Suppliers;

public enum SupplierDecision
{
    Approve,
    Reject,
    Block,
    Unblock,
}

/// <summary>An admin decision on a supplier. Reject and Block need a reason.</summary>
public sealed record DecideSupplierCommand(Guid SupplierId, SupplierDecision Decision, string? Reason = null) : ICommand;

internal sealed class DecideSupplierCommandValidator : AbstractValidator<DecideSupplierCommand>
{
    public DecideSupplierCommandValidator()
    {
        RuleFor(x => x.Reason)
            .NotEmpty()
            .MaximumLength(Supplier.ReasonMaxLength)
            .When(x => x.Decision is SupplierDecision.Reject or SupplierDecision.Block);
    }
}

internal sealed class DecideSupplierCommandHandler(IAppDbContext db, ICurrentUser currentUser, IClock clock)
    : ICommandHandler<DecideSupplierCommand>
{
    public async Task<Result> Handle(DecideSupplierCommand command, CancellationToken cancellationToken)
    {
        var supplier = await db.Suppliers.SingleOrDefaultAsync(s => s.Id == command.SupplierId, cancellationToken);
        if (supplier is null)
        {
            return SupplierErrors.NotFound;
        }

        var result = command.Decision switch
        {
            SupplierDecision.Approve => supplier.Approve(currentUser.UserId!.Value, clock.Today, clock.UtcNow),
            SupplierDecision.Reject => supplier.Reject(command.Reason!),
            SupplierDecision.Block => supplier.Block(command.Reason!),
            SupplierDecision.Unblock => supplier.Unblock(),
            _ => throw new ArgumentOutOfRangeException(nameof(command), command.Decision, "Unknown decision."),
        };

        if (result.IsSuccess)
        {
            await db.SaveChangesAsync(cancellationToken);
        }

        return result;
    }
}

/// <summary>An admin verifies a pending bank account (four-eyes rule).</summary>
public sealed record VerifyBankAccountCommand(Guid SupplierId, Guid BankAccountId) : ICommand;

internal sealed class VerifyBankAccountCommandHandler(IAppDbContext db, ICurrentUser currentUser, IClock clock)
    : ICommandHandler<VerifyBankAccountCommand>
{
    public async Task<Result> Handle(VerifyBankAccountCommand command, CancellationToken cancellationToken)
    {
        var supplier = await db.Suppliers.SingleOrDefaultAsync(s => s.Id == command.SupplierId, cancellationToken);
        if (supplier is null)
        {
            return SupplierErrors.NotFound;
        }

        var result = supplier.VerifyBankAccount(command.BankAccountId, currentUser.UserId!.Value, clock.UtcNow);
        if (result.IsSuccess)
        {
            await db.SaveChangesAsync(cancellationToken);
        }

        return result;
    }
}
