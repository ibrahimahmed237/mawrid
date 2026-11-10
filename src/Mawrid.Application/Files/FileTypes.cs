namespace Mawrid.Application.Files;

/// <summary>
/// Which files we accept. The declared content type must match the first bytes of the
/// file ("magic bytes"), so a renamed .exe cannot pass as a PDF.
/// </summary>
public static class FileTypes
{
    public const long MaxSizeBytes = 25 * 1024 * 1024;

    private static readonly Dictionary<string, byte[][]> Signatures = new(StringComparer.OrdinalIgnoreCase)
    {
        ["application/pdf"] = [[0x25, 0x50, 0x44, 0x46]], // %PDF
        ["image/png"] = [[0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A]],
        ["image/jpeg"] = [[0xFF, 0xD8, 0xFF]],
        ["application/xml"] = [[0x3C], [0xEF, 0xBB, 0xBF, 0x3C]], // "<", or a UTF-8 BOM then "<"
        ["text/xml"] = [[0x3C], [0xEF, 0xBB, 0xBF, 0x3C]],
    };

    public static IReadOnlyCollection<string> Allowed => Signatures.Keys;

    public static bool IsAllowed(string contentType) => Signatures.ContainsKey(contentType);

    public static bool MatchesSignature(string contentType, ReadOnlySpan<byte> header)
    {
        if (!Signatures.TryGetValue(contentType, out var signatures))
        {
            return false;
        }

        foreach (var signature in signatures)
        {
            if (header.StartsWith(signature))
            {
                return true;
            }
        }

        return false;
    }
}
