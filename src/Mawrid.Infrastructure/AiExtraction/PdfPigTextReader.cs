using System.Text;
using Mawrid.Application.Payables.Extraction;
using UglyToad.PdfPig;

namespace Mawrid.Infrastructure.AiExtraction;

/// <summary>Reads the text layer with PdfPig (Apache 2.0). Words are joined per page, pages by blank lines.</summary>
internal sealed class PdfPigTextReader : IPdfTextReader
{
    public const int MaxPages = 20;

    public async Task<string?> ReadTextAsync(Stream pdf, CancellationToken cancellationToken)
    {
        // PdfPig needs a seekable stream; blob streams are not.
        using var buffer = new MemoryStream();
        await pdf.CopyToAsync(buffer, cancellationToken);
        buffer.Position = 0;

        try
        {
            using var document = PdfDocument.Open(buffer);
            var text = new StringBuilder();
            foreach (var page in document.GetPages().Take(MaxPages))
            {
                text.AppendLine(string.Join(' ', page.GetWords().Select(w => w.Text)));
                text.AppendLine();
            }

            return text.ToString().Trim();
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            return null;
        }
    }
}
