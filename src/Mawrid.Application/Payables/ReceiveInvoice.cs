using FluentValidation;
using Mawrid.Application.Common.Abstractions;
using Mawrid.Application.Common.Messaging;
using Mawrid.Application.Common.Persistence;
using Mawrid.Application.Common.Security;
using Mawrid.Application.Contracts.Files;
using Mawrid.Application.Contracts.Purchasing;
using Mawrid.Application.Contracts.Suppliers;
using Mawrid.Domain.Common;
using Mawrid.Domain.Payables;
using Microsoft.EntityFrameworkCore;

namespace Mawrid.Application.Payables;

/// <summary>
/// The supplier sends its e-invoice: a UBL 2.1 XML file (uploaded first through /files) and,
/// optionally, the PDF copy. The invoice is checked at once and comes back Matched or OnHold.
/// </summary>
public sealed record SubmitInvoiceCommand(Guid XmlFileId, Guid? PdfFileId) : ICommand<Guid>;

internal sealed class SubmitInvoiceCommandHandler(
    IAppDbContext db, ICurrentUser currentUser, IFileReferences files, IPurchaseOrderInvoicing purchaseOrders, ISupplierDirectory suppliers,
    InvoiceChecks checks, IClock clock)
    : ICommandHandler<SubmitInvoiceCommand, Guid>
{
    public async Task<Result<Guid>> Handle(SubmitInvoiceCommand command, CancellationToken cancellationToken)
    {
        var supplierId = currentUser.SupplierId!.Value;
        if (command.PdfFileId is { } pdfId && !await files.CanAttachAsync(pdfId, cancellationToken))
        {
            return PayablesErrors.SourceFileInvalid;
        }

        var xml = await files.OpenAsync(command.XmlFileId, cancellationToken);
        if (xml is null || !xml.ContentType.EndsWith("xml", StringComparison.OrdinalIgnoreCase))
        {
            return PayablesErrors.SourceFileInvalid;
        }

        Result<UblInvoice> parsed;
        await using (xml.Content)
        {
            parsed = UblInvoiceReader.Read(xml.Content);
        }

        if (parsed.IsFailure)
        {
            return parsed.Error!;
        }

        // The seller in the XML must be the supplier who sends it.
        var supplier = await suppliers.FindAsync(supplierId, cancellationToken);
        if (parsed.Value.SellerVatNumber is { } sellerVat && sellerVat != supplier?.VatNumber)
        {
            return PayablesErrors.SellerVatMismatch;
        }

        // A PO of another supplier gets the same answer as an unknown PO: suppliers must not probe numbers.
        var order = await purchaseOrders.FindByNumberAsync(parsed.Value.PurchaseOrderNumber, cancellationToken);
        if (order is not { AcceptsInvoices: true } || order.SupplierId != supplierId)
        {
            return PayablesErrors.PurchaseOrderNotInvoiceable;
        }

        return await InvoiceIntake.ReceiveAsync(db, checks, clock, order, parsed.Value.Details, InvoiceSource.UblXml,
            command.XmlFileId, command.PdfFileId, currentUser, cancellationToken);
    }
}

public sealed record InvoiceLineDto(int PoLineNumber, string Description, decimal Quantity, decimal UnitPrice, string VatCategory, decimal NetAmount, decimal VatAmount);

/// <summary>
/// An AP clerk types in an invoice that arrived only as a PDF, often from an AI draft
/// (<c>POST /invoices/extract</c>). Set <see cref="ExtractedWithAi"/> when the values came from a draft.
/// </summary>
public sealed record EnterInvoiceCommand(
    Guid PurchaseOrderId,
    string SupplierInvoiceNumber,
    DateOnly IssueDate,
    IReadOnlyList<InvoiceLineDto> Lines,
    decimal NetTotal,
    decimal VatTotal,
    decimal Total,
    string? QrCode,
    Guid PdfFileId,
    bool ExtractedWithAi = false) : ICommand<Guid>;

internal sealed class EnterInvoiceCommandValidator : AbstractValidator<EnterInvoiceCommand>
{
    public EnterInvoiceCommandValidator()
    {
        RuleFor(x => x.SupplierInvoiceNumber).NotEmpty().MaximumLength(SupplierInvoice.NumberMaxLength);
        RuleFor(x => x.Lines).NotEmpty();
        RuleFor(x => x.QrCode).MaximumLength(ZatcaQr.MaxLength);
        RuleForEach(x => x.Lines).ChildRules(line =>
        {
            line.RuleFor(l => l.Description).NotEmpty().MaximumLength(SupplierInvoiceLine.DescriptionMaxLength);
            line.RuleFor(l => l.Quantity).GreaterThan(0).PrecisionScale(18, 3, ignoreTrailingZeros: true);
            line.RuleFor(l => l.UnitPrice).GreaterThanOrEqualTo(0).PrecisionScale(19, 4, ignoreTrailingZeros: true);
            line.RuleFor(l => l.NetAmount).GreaterThanOrEqualTo(0).PrecisionScale(19, 2, ignoreTrailingZeros: true);
            line.RuleFor(l => l.VatAmount).GreaterThanOrEqualTo(0).PrecisionScale(19, 2, ignoreTrailingZeros: true);
            line.RuleFor(l => l.VatCategory).Must(v => Enum.TryParse<VatCategory>(v, ignoreCase: true, out _))
                .WithMessage($"VAT category must be one of: {string.Join(", ", Enum.GetNames<VatCategory>())}.");
        });
    }
}

internal sealed class EnterInvoiceCommandHandler(
    IAppDbContext db, ICurrentUser currentUser, IFileReferences files, IPurchaseOrderInvoicing purchaseOrders, InvoiceChecks checks, IClock clock)
    : ICommandHandler<EnterInvoiceCommand, Guid>
{
    public async Task<Result<Guid>> Handle(EnterInvoiceCommand command, CancellationToken cancellationToken)
    {
        if (!await files.CanAttachAsync(command.PdfFileId, cancellationToken))
        {
            return PayablesErrors.SourceFileInvalid;
        }

        var order = await purchaseOrders.FindAsync(command.PurchaseOrderId, cancellationToken);
        if (order is not { AcceptsInvoices: true })
        {
            return PayablesErrors.PurchaseOrderNotInvoiceable;
        }

        var details = new InvoiceDetails(
            command.SupplierInvoiceNumber,
            command.IssueDate,
            command.Lines.Select(l => new InvoiceLineInput(l.PoLineNumber, l.Description, l.Quantity, l.UnitPrice,
                Enum.Parse<VatCategory>(l.VatCategory, ignoreCase: true), l.NetAmount, l.VatAmount)).ToList(),
            command.NetTotal,
            command.VatTotal,
            command.Total,
            command.QrCode);

        var source = command.ExtractedWithAi ? InvoiceSource.AiAssisted : InvoiceSource.Manual;
        return await InvoiceIntake.ReceiveAsync(db, checks, clock, order, details, source,
            null, command.PdfFileId, currentUser, cancellationToken);
    }
}

internal static class InvoiceIntake
{
    /// <summary>Refuses duplicates, creates the invoice, checks it and saves it.</summary>
    public static async Task<Result<Guid>> ReceiveAsync(
        IAppDbContext db, InvoiceChecks checks, IClock clock, PurchaseOrderForInvoice order, InvoiceDetails details, InvoiceSource source,
        Guid? xmlFileId, Guid? pdfFileId, ICurrentUser receivedBy, CancellationToken cancellationToken)
    {
        var number = details.SupplierInvoiceNumber.Trim();
        if (await db.SupplierInvoices.AnyAsync(i => i.SupplierId == order.SupplierId && i.SupplierInvoiceNumber == number, cancellationToken))
        {
            return PayablesErrors.Duplicate;
        }

        var received = SupplierInvoice.Receive(receivedBy.OrganizationId!.Value, order.SupplierId, order.Id, details, source,
            xmlFileId, pdfFileId, receivedBy.UserId!.Value, clock.Today, clock.UtcNow);
        if (received.IsFailure)
        {
            return received.Error!;
        }

        var invoice = received.Value;
        db.SupplierInvoices.Add(invoice);
        await checks.RunAsync(invoice, order, cancellationToken);
        await db.SaveChangesAsync(cancellationToken);
        return invoice.Id;
    }
}
