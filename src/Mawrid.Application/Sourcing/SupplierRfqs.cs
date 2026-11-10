using FluentValidation;
using Mawrid.Application.Common.Abstractions;
using Mawrid.Application.Common.Messaging;
using Mawrid.Application.Common.Persistence;
using Mawrid.Application.Common.Security;
using Mawrid.Application.Contracts.Files;
using Mawrid.Domain.Common;
using Mawrid.Domain.Sourcing;
using Microsoft.EntityFrameworkCore;

namespace Mawrid.Application.Sourcing;

// The supplier side of RFQs. A supplier sees only RFQs it is invited to, and only its own quotation.

public sealed record SupplierRfqDto(
    Guid Id, string Number, string Title, string? Description, string Status, DateTimeOffset? Deadline, int Revision, bool IsOpen,
    IReadOnlyList<RfqItemDto> Items, MyQuotationDto? MyQuotation, IReadOnlyList<int> ItemsAwardedToMe);

public sealed record MyQuotationDto(Guid Id, int Version, DateTimeOffset SubmittedAtUtc, DateOnly ValidUntil, bool NeedsReconfirmation, string? Notes,
    IReadOnlyList<QuotationLineDto> Lines, IReadOnlyList<Guid> AttachmentFileIds);

public sealed record QuotationLineDto(int ItemLineNumber, decimal UnitPrice, string VatCategory, int LeadTimeDays);

public sealed record GetMyRfqsQuery : IQuery<IReadOnlyList<SupplierRfqDto>>;

internal sealed class GetMyRfqsQueryHandler(IAppDbContext db, ICurrentUser currentUser, IClock clock) : IQueryHandler<GetMyRfqsQuery, IReadOnlyList<SupplierRfqDto>>
{
    public async Task<Result<IReadOnlyList<SupplierRfqDto>>> Handle(GetMyRfqsQuery query, CancellationToken cancellationToken)
    {
        var supplierId = currentUser.SupplierId!.Value;
        var rfqs = await SupplierRfqAccess.Visible(db, supplierId).OrderByDescending(r => r.Deadline).ToListAsync(cancellationToken);
        var rfqIds = rfqs.Select(r => r.Id).ToList();
        var quotations = await db.Quotations.AsNoTracking().Where(q => q.SupplierId == supplierId && rfqIds.Contains(q.RfqId)).ToListAsync(cancellationToken);
        var now = clock.UtcNow;
        return rfqs.Select(r => SupplierRfqAccess.ToDto(r, quotations.SingleOrDefault(q => q.RfqId == r.Id), supplierId, now)).ToList();
    }
}

public sealed record GetMyRfqQuery(Guid RfqId) : IQuery<SupplierRfqDto>;

internal sealed class GetMyRfqQueryHandler(IAppDbContext db, ICurrentUser currentUser, IClock clock) : IQueryHandler<GetMyRfqQuery, SupplierRfqDto>
{
    public async Task<Result<SupplierRfqDto>> Handle(GetMyRfqQuery query, CancellationToken cancellationToken)
    {
        var supplierId = currentUser.SupplierId!.Value;
        var rfq = await SupplierRfqAccess.Visible(db, supplierId).SingleOrDefaultAsync(r => r.Id == query.RfqId, cancellationToken);
        if (rfq is null)
        {
            return SourcingErrors.NotFound;
        }

        var quotation = await db.Quotations.AsNoTracking().SingleOrDefaultAsync(q => q.RfqId == rfq.Id && q.SupplierId == supplierId, cancellationToken);
        return SupplierRfqAccess.ToDto(rfq, quotation, supplierId, clock.UtcNow);
    }
}

/// <summary>Submits or revises the supplier's quotation while the RFQ is open.</summary>
public sealed record SubmitQuotationCommand(
    Guid RfqId, DateOnly ValidUntil, IReadOnlyList<QuotationLineDto> Lines, string? Notes, IReadOnlyList<Guid>? AttachmentFileIds) : ICommand<Guid>;

internal sealed class SubmitQuotationCommandValidator : AbstractValidator<SubmitQuotationCommand>
{
    public SubmitQuotationCommandValidator()
    {
        RuleFor(x => x.Lines).NotEmpty();
        RuleForEach(x => x.Lines).ChildRules(line =>
        {
            line.RuleFor(l => l.UnitPrice).GreaterThan(0);
            line.RuleFor(l => l.VatCategory).Must(v => Enum.TryParse<VatCategory>(v, ignoreCase: true, out _))
                .WithMessage($"VAT category must be one of: {string.Join(", ", Enum.GetNames<VatCategory>())}.");
        });
        RuleFor(x => x.Notes).MaximumLength(Quotation.NotesMaxLength);
        RuleFor(x => x.AttachmentFileIds).Must(ids => ids is null || ids.Count <= 10);
    }
}

internal sealed class SubmitQuotationCommandHandler(IAppDbContext db, ICurrentUser currentUser, IFileReferences files, IClock clock)
    : ICommandHandler<SubmitQuotationCommand, Guid>
{
    public async Task<Result<Guid>> Handle(SubmitQuotationCommand command, CancellationToken cancellationToken)
    {
        var supplierId = currentUser.SupplierId!.Value;
        var rfq = await SupplierRfqAccess.Visible(db, supplierId).SingleOrDefaultAsync(r => r.Id == command.RfqId, cancellationToken);
        if (rfq is null)
        {
            return SourcingErrors.NotFound;
        }

        var attachments = command.AttachmentFileIds ?? [];
        foreach (var fileId in attachments)
        {
            if (!await files.CanAttachAsync(fileId, cancellationToken))
            {
                return SourcingErrors.AttachmentNotFound;
            }
        }

        var input = new QuotationInput(command.ValidUntil,
            command.Lines.Select(l => new QuotationLineInput(l.ItemLineNumber, l.UnitPrice, Enum.Parse<VatCategory>(l.VatCategory, ignoreCase: true), l.LeadTimeDays)).ToList(),
            command.Notes, attachments);

        var now = clock.UtcNow;
        var quotation = await db.Quotations.SingleOrDefaultAsync(q => q.RfqId == rfq.Id && q.SupplierId == supplierId, cancellationToken);
        if (quotation is null)
        {
            var submitted = Quotation.Submit(rfq, supplierId, input, now, clock.Today);
            if (submitted.IsFailure)
            {
                return submitted.Error!;
            }

            quotation = submitted.Value;
            db.Quotations.Add(quotation);
        }
        else
        {
            var revised = quotation.Revise(rfq, input, now, clock.Today);
            if (revised.IsFailure)
            {
                return revised.Error!;
            }
        }

        await db.SaveChangesAsync(cancellationToken);
        return quotation.Id;
    }
}

internal static class SupplierRfqAccess
{
    /// <summary>RFQs this supplier is invited to, once published. Drafts are never shown to suppliers.</summary>
    public static IQueryable<Rfq> Visible(IAppDbContext db, Guid supplierId) =>
        db.Rfqs.AsNoTracking().Where(r => r.Status != RfqStatus.Draft && r.Invitations.Any(i => i.SupplierId == supplierId));

    public static SupplierRfqDto ToDto(Rfq rfq, Quotation? quotation, Guid supplierId, DateTimeOffset now) => new(
        rfq.Id, rfq.Number, rfq.Title, rfq.Description, rfq.Status.ToString(), rfq.Deadline, rfq.Revision, rfq.IsOpen(now),
        rfq.Items.OrderBy(i => i.LineNumber).Select(i => new RfqItemDto(i.LineNumber, i.Description, i.Quantity, i.Unit, null)).ToList(),
        quotation is null ? null : new MyQuotationDto(quotation.Id, quotation.Version, quotation.SubmittedAtUtc, quotation.ValidUntil,
            quotation.NeedsReconfirmation(rfq), quotation.Notes,
            quotation.Lines.OrderBy(l => l.ItemLineNumber).Select(l => new QuotationLineDto(l.ItemLineNumber, l.UnitPrice, l.VatCategory.ToString(), l.LeadTimeDays)).ToList(),
            quotation.AttachmentFileIds),
        rfq.Awards.Where(a => a.SupplierId == supplierId).Select(a => a.ItemLineNumber).Order().ToList());
}
