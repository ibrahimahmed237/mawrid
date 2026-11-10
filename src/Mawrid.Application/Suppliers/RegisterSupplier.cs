using FluentValidation;
using Mawrid.Application.Common.Messaging;
using Mawrid.Application.Common.Persistence;
using Mawrid.Application.Common.Security;
using Mawrid.Domain.Common;
using Mawrid.Domain.Suppliers;
using Microsoft.EntityFrameworkCore;

namespace Mawrid.Application.Suppliers;

/// <summary>Procurement adds a supplier. Its contact is then invited as a supplier user to finish onboarding.</summary>
public sealed record RegisterSupplierCommand(
    string NameAr,
    string NameEn,
    string VatNumber,
    string CrNumber,
    SupplierContactDto Contact) : ICommand<Guid>;

internal sealed class RegisterSupplierCommandValidator : AbstractValidator<RegisterSupplierCommand>
{
    public RegisterSupplierCommandValidator()
    {
        RuleFor(x => x.NameAr).NotEmpty().MaximumLength(Supplier.NameMaxLength);
        RuleFor(x => x.NameEn).NotEmpty().MaximumLength(Supplier.NameMaxLength);
        RuleFor(x => x.VatNumber).NotEmpty();
        RuleFor(x => x.CrNumber).NotEmpty();
        RuleFor(x => x.Contact).NotNull();
        RuleFor(x => x.Contact.Name).NotEmpty().MaximumLength(200).When(x => x.Contact is not null);
        RuleFor(x => x.Contact.Email).NotEmpty().EmailAddress().MaximumLength(256).When(x => x.Contact is not null);
        RuleFor(x => x.Contact.Phone).MaximumLength(30).When(x => x.Contact is not null);
    }
}

internal sealed class RegisterSupplierCommandHandler(IAppDbContext db, ICurrentUser currentUser)
    : ICommandHandler<RegisterSupplierCommand, Guid>
{
    public async Task<Result<Guid>> Handle(RegisterSupplierCommand command, CancellationToken cancellationToken)
    {
        var organizationId = currentUser.OrganizationId
            ?? throw new InvalidOperationException("Registering a supplier needs an organization context.");

        var contact = new SupplierContact(command.Contact.Name.Trim(), command.Contact.Email.Trim().ToLowerInvariant(), command.Contact.Phone?.Trim());
        var registered = Supplier.Register(organizationId, command.NameAr, command.NameEn, command.VatNumber, command.CrNumber, contact);
        if (registered.IsFailure)
        {
            return registered.Error!;
        }

        var supplier = registered.Value;
        if (await db.Suppliers.AnyAsync(s => s.VatNumber == supplier.VatNumber, cancellationToken))
        {
            return SupplierErrors.VatNumberTaken;
        }

        db.Suppliers.Add(supplier);
        await db.SaveChangesAsync(cancellationToken);
        return supplier.Id;
    }
}
