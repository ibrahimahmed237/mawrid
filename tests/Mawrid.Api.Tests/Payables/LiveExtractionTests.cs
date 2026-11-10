using Mawrid.Application.Contracts.Purchasing;
using Mawrid.Application.Payables.Extraction;
using Mawrid.Domain.Common;
using Mawrid.Infrastructure.AiExtraction;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace Mawrid.Api.Tests.Payables;

/// <summary>Runs only when MAWRID_LIVE_AI is set (for example to "Pollinations"), because it calls a real AI service.</summary>
public sealed class LiveAiFactAttribute : FactAttribute
{
    public LiveAiFactAttribute()
    {
        if (string.IsNullOrEmpty(Environment.GetEnvironmentVariable("MAWRID_LIVE_AI")))
        {
            Skip = "Set MAWRID_LIVE_AI=Pollinations (or Ollama/OpenAI) to call a real AI provider.";
        }
    }
}

/// <summary>Checks the prompt with a real model. Uses a made-up invoice only: never send real invoices to a free service.</summary>
public sealed class LiveExtractionTests
{
    [LiveAiFact]
    public async Task A_real_model_reads_a_made_up_invoice()
    {
        var provider = Enum.Parse<AiProvider>(Environment.GetEnvironmentVariable("MAWRID_LIVE_AI")!, ignoreCase: true);
        var options = Options.Create(new AiExtractionOptions { Provider = provider, ApiKey = Environment.GetEnvironmentVariable("MAWRID_LIVE_AI_KEY") });
        var client = new OpenAiCompatibleLlmClient(new HttpClient { Timeout = TimeSpan.FromMinutes(2) }, options, NullLogger<OpenAiCompatibleLlmClient>.Instance);
        var order = new PurchaseOrderForInvoice(Guid.NewGuid(), "PO-2026-000012", Guid.NewGuid(), Guid.NewGuid(), true,
        [
            new PurchaseOrderLineForInvoice(1, "Laptop 14 inch", 3_900m, VatCategory.Standard, 6m),
            new PurchaseOrderLineForInvoice(2, "Docking station", 500m, VatCategory.Standard, 10m),
        ]);
        const string Text = """
            Example Trading Co. - TAX INVOICE / فاتورة ضريبية
            VAT Registration No: 310122393500003
            Invoice No: EX-2026-0042   Invoice Date: 09/11/2026   Customer PO: PO-2026-000012
            #  Description        Qty  Unit Price  Net        VAT 15%
            1  Laptop 14 inch     6    3,900.00    23,400.00  3,510.00
            2  Docking station    10   500.00      5,000.00   750.00
            Total excluding VAT: 28,400.00 SAR   Total VAT: 4,260.00 SAR   Total amount due: 32,660.00 SAR
            """;

        var answer = await client.CompleteJsonAsync(InvoiceExtractionPrompt.System, InvoiceExtractionPrompt.User(Text, order), CancellationToken.None);
        var draft = InvoiceDraftBuilder.Build(answer.Value, order, "310122393500003", new DateOnly(2026, 11, 10)).Value;

        draft.SupplierInvoiceNumber.Value.Should().Be("EX-2026-0042");
        draft.IssueDate.Value.Should().Be(new DateOnly(2026, 11, 9));
        draft.SellerVatNumber.Value.Should().Be("310122393500003");
        draft.Total.Value.Should().Be(32_660m);
        draft.Lines.Select(l => l.PoLineNumber).Should().Equal(1, 2);
        draft.Warnings.Should().BeEmpty();
    }
}
