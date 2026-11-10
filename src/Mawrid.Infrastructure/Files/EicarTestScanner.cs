using System.Text;
using Mawrid.Application.Files;

namespace Mawrid.Infrastructure.Files;

/// <summary>
/// A stand-in scanner for development and tests. It only detects the EICAR test file,
/// the industry-standard harmless string that every antivirus reports as a virus.
/// Production replaces this with a real scanner (for example Microsoft Defender for
/// Storage or ClamAV) behind the same <see cref="IMalwareScanner"/> interface.
/// </summary>
public sealed class EicarTestScanner : IMalwareScanner
{
    private static readonly byte[] EicarSignature =
        Encoding.ASCII.GetBytes(@"EICAR-STANDARD-ANTIVIRUS-TEST-FILE");

    public async Task<MalwareScanResult> ScanAsync(Stream content, CancellationToken cancellationToken)
    {
        using var buffer = new MemoryStream();
        await content.CopyToAsync(buffer, cancellationToken);

        return buffer.GetBuffer().AsSpan(0, (int)buffer.Length).IndexOf(EicarSignature) >= 0
            ? new MalwareScanResult(false, "EICAR-Test-File")
            : MalwareScanResult.Clean;
    }
}
