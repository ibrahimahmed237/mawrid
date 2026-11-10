using Mawrid.Application.Common.Abstractions;
using Mawrid.Application.Common.Messaging;
using Mawrid.Application.Contracts.Files;
using Mawrid.Application.Contracts.Purchasing;
using Mawrid.Application.Contracts.Suppliers;
using Mawrid.Domain.Common;
using Mawrid.Domain.Payables;

namespace Mawrid.Application.Payables.Extraction;

/// <summary>
/// Reads a PDF invoice with AI and returns a draft for the AP clerk to confirm. Nothing is saved.
/// Give the PO when you know it: the model then maps invoice lines to PO lines.
/// </summary>
public sealed record ExtractInvoiceCommand(Guid PdfFileId, Guid? PurchaseOrderId) : ICommand<InvoiceDraftDto>;

internal sealed class ExtractInvoiceCommandHandler(
    IFileReferences files, IPdfTextReader pdfReader, ILlmClient llm, IPurchaseOrderInvoicing purchaseOrders, ISupplierDirectory suppliers, IClock clock)
    : ICommandHandler<ExtractInvoiceCommand, InvoiceDraftDto>
{
    public async Task<Result<InvoiceDraftDto>> Handle(ExtractInvoiceCommand command, CancellationToken cancellationToken)
    {
        PurchaseOrderForInvoice? order = null;
        if (command.PurchaseOrderId is { } poId)
        {
            order = await purchaseOrders.FindAsync(poId, cancellationToken);
            if (order is not { AcceptsInvoices: true })
            {
                return PayablesErrors.PurchaseOrderNotInvoiceable;
            }
        }

        var file = await files.OpenAsync(command.PdfFileId, cancellationToken);
        if (file is null || file.ContentType != "application/pdf")
        {
            return ExtractionErrors.NotAPdf;
        }

        string? text;
        await using (file.Content)
        {
            text = await pdfReader.ReadTextAsync(file.Content, cancellationToken);
        }

        if (text is null)
        {
            return ExtractionErrors.NotAPdf;
        }

        if (string.IsNullOrWhiteSpace(text))
        {
            return ExtractionErrors.NoText;
        }

        var truncated = text.Length > InvoiceExtractionPrompt.MaxInvoiceCharacters;
        var answer = await llm.CompleteJsonAsync(InvoiceExtractionPrompt.System,
            InvoiceExtractionPrompt.User(truncated ? text[..InvoiceExtractionPrompt.MaxInvoiceCharacters] : text, order), cancellationToken);
        if (answer.IsFailure)
        {
            return answer.Error!;
        }

        string? supplierVat = null;
        if (order is not null)
        {
            supplierVat = (await suppliers.FindAsync(order.SupplierId, cancellationToken))?.VatNumber;
        }

        var draft = InvoiceDraftBuilder.Build(answer.Value, order, supplierVat, clock.Today);
        if (draft.IsSuccess && truncated)
        {
            draft = draft.Value with { Warnings = [.. draft.Value.Warnings, "The PDF is long; only the first part was read."] };
        }

        return draft;
    }
}
