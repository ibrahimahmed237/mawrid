namespace Mawrid.Application.Files;

/// <summary>Where file bytes are kept (Azure Blob Storage in production, Azurite locally).</summary>
public interface IFileStorage
{
    Task SaveAsync(string key, Stream content, string contentType, CancellationToken cancellationToken);

    Task<Stream?> OpenReadAsync(string key, CancellationToken cancellationToken);
}

/// <summary>Checks uploaded content for malware before anyone can download it.</summary>
public interface IMalwareScanner
{
    Task<MalwareScanResult> ScanAsync(Stream content, CancellationToken cancellationToken);
}

public sealed record MalwareScanResult(bool IsClean, string? Threat = null)
{
    public static MalwareScanResult Clean { get; } = new(true);
}
