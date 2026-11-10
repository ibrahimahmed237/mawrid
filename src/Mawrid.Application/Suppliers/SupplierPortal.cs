using FluentValidation;
using Mawrid.Application.Common.Abstractions;
using Mawrid.Application.Common.Messaging;
using Mawrid.Application.Common.Persistence;
using Mawrid.Application.Common.Security;
using Mawrid.Application.Contracts.Files;
using Mawrid.Domain.Common;
using Mawrid.Domain.Suppliers;
using Microsoft.EntityFrameworkCore;

namespace Mawrid.Application.Suppliers;

// Commands a supplier user runs for their own company. The supplier id always comes
// from the access token, never from the request.

public sealed record GetMySupplierQuery : IQuery<SupplierDto>;

internal sealed class GetMySupplierQueryHandler(IAppDbContext db, ICurrentUser currentUser, IClock clock)
    : IQueryHandler<GetMySupplierQuery, SupplierDto>
{
    public async Task<Result<SupplierDto>> Handle(GetMySupplierQuery query, CancellationToken cancellationToken)
    {
        var supplier = await db.Suppliers.AsNoTracking().SingleOrDefaultAsync(s => s.Id == currentUser.SupplierId, cancellationToken);
        return supplier is null ? SupplierErrors.NotFound : SupplierMapping.ToDto(supplier, clock.Today);
    }
}

public sealed record AddSupplierDocumentCommand(string Type, Guid FileId, DateOnly ExpiresOn) : ICommand;

internal sealed class AddSupplierDocumentCommandValidator : AbstractValidator<AddSupplierDocumentCommand>
{
    public AddSupplierDocumentCommandValidator()
    {
        RuleFor(x => x.Type)
            .Must(t => Enum.TryParse<SupplierDocumentType>(t, ignoreCase: true, out var parsed) && Enum.IsDefined(parsed))
            .WithMessage($"Type must be one of: {string.Join(", ", Enum.GetNames<SupplierDocumentType>())}.");
        RuleFor(x => x.FileId).NotEmpty();
    }
}

internal sealed class AddSupplierDocumentCommandHandler(
    IAppDbContext db, ICurrentUser currentUser, IFileReferences files, IClock clock) : ICommandHandler<AddSupplierDocumentCommand>
{
    public async Task<Result> Handle(AddSupplierDocumentCommand command, CancellationToken cancellationToken)
    {
        var supplier = await db.Suppliers.SingleOrDefaultAsync(s => s.Id == currentUser.SupplierId, cancellationToken);
        if (supplier is null)
        {
            return SupplierErrors.NotFound;
        }

        if (!await files.CanAttachAsync(command.FileId, cancellationToken))
        {
            return SupplierErrors.FileNotFound;
        }

        var type = Enum.Parse<SupplierDocumentType>(command.Type, ignoreCase: true);
        var result = supplier.AddDocument(type, command.FileId, command.ExpiresOn, clock.Today, clock.UtcNow);
        if (result.IsSuccess)
        {
            await db.SaveChangesAsync(cancellationToken);
        }

        return result;
    }
}

public sealed record RequestBankAccountCommand(string Iban, string BankName, string AccountName) : ICommand;

internal sealed class RequestBankAccountCommandValidator : AbstractValidator<RequestBankAccountCommand>
{
    public RequestBankAccountCommandValidator()
    {
        RuleFor(x => x.Iban).NotEmpty().MaximumLength(40);
        RuleFor(x => x.BankName).NotEmpty().MaximumLength(Supplier.NameMaxLength);
        RuleFor(x => x.AccountName).NotEmpty().MaximumLength(Supplier.NameMaxLength);
    }
}

internal sealed class RequestBankAccountCommandHandler(IAppDbContext db, ICurrentUser currentUser, IClock clock)
    : ICommandHandler<RequestBankAccountCommand>
{
    public async Task<Result> Handle(RequestBankAccountCommand command, CancellationToken cancellationToken)
    {
        var supplier = await db.Suppliers.SingleOrDefaultAsync(s => s.Id == currentUser.SupplierId, cancellationToken);
        if (supplier is null)
        {
            return SupplierErrors.NotFound;
        }

        var result = supplier.RequestBankAccount(command.Iban, command.BankName, command.AccountName, currentUser.UserId, clock.UtcNow);
        if (result.IsSuccess)
        {
            await db.SaveChangesAsync(cancellationToken);
        }

        return result;
    }
}

public sealed record SubmitSupplierForReviewCommand : ICommand;

internal sealed class SubmitSupplierForReviewCommandHandler(IAppDbContext db, ICurrentUser currentUser, IClock clock)
    : ICommandHandler<SubmitSupplierForReviewCommand>
{
    public async Task<Result> Handle(SubmitSupplierForReviewCommand command, CancellationToken cancellationToken)
    {
        var supplier = await db.Suppliers.SingleOrDefaultAsync(s => s.Id == currentUser.SupplierId, cancellationToken);
        if (supplier is null)
        {
            return SupplierErrors.NotFound;
        }

        var result = supplier.SubmitForReview(clock.Today);
        if (result.IsSuccess)
        {
            await db.SaveChangesAsync(cancellationToken);
        }

        return result;
    }
}
