using Mawrid.Domain.Common;

namespace Mawrid.Domain.Files;

/// <summary>
/// The record of an uploaded file. The bytes live in blob storage under <see cref="StorageKey"/>.
/// A file can be read only after the virus scan marked it <see cref="FileScanStatus.Clean"/>.
/// </summary>
public sealed class StoredFile : AggregateRoot<Guid>, ITenantOwned
{
    public const int FileNameMaxLength = 255;

    private StoredFile(
        Guid id, Guid organizationId, string fileName, string contentType, long sizeBytes, string sha256,
        Guid? uploadedBy, Guid? uploaderSupplierId, DateTimeOffset uploadedAtUtc) : base(id)
    {
        OrganizationId = organizationId;
        FileName = fileName;
        ContentType = contentType;
        SizeBytes = sizeBytes;
        Sha256 = sha256;
        UploadedBy = uploadedBy;
        UploaderSupplierId = uploaderSupplierId;
        UploadedAtUtc = uploadedAtUtc;
        StorageKey = $"{organizationId:N}/{uploadedAtUtc:yyyy/MM}/{id:N}";
        ScanStatus = FileScanStatus.PendingScan;
    }

    private StoredFile()
    {
        FileName = null!;
        ContentType = null!;
        Sha256 = null!;
        StorageKey = null!;
    }

    public Guid OrganizationId { get; private set; }

    public string FileName { get; private set; }

    public string ContentType { get; private set; }

    public long SizeBytes { get; private set; }

    /// <summary>Hex SHA-256 of the content. Useful to spot the same file uploaded twice.</summary>
    public string Sha256 { get; private set; }

    public string StorageKey { get; private set; }

    public FileScanStatus ScanStatus { get; private set; }

    public string? ScanResult { get; private set; }

    public Guid? UploadedBy { get; private set; }

    /// <summary>Set when a supplier user uploaded the file. Supplier users can read only their own company's files.</summary>
    public Guid? UploaderSupplierId { get; private set; }

    public DateTimeOffset UploadedAtUtc { get; private set; }

    public static StoredFile Create(
        Guid organizationId, string fileName, string contentType, long sizeBytes, string sha256,
        Guid? uploadedBy, Guid? uploaderSupplierId, DateTimeOffset now) =>
        new(Guid.CreateVersion7(), organizationId, SafeFileName(fileName), contentType, sizeBytes, sha256, uploadedBy, uploaderSupplierId, now);

    public void MarkClean() => ScanStatus = FileScanStatus.Clean;

    public void MarkRejected(string reason)
    {
        ScanStatus = FileScanStatus.Rejected;
        ScanResult = reason.Length > 200 ? reason[..200] : reason;
    }

    /// <summary>Keeps only the name part (no folders) and removes control characters.</summary>
    private static string SafeFileName(string fileName)
    {
        var name = Path.GetFileName(fileName.Replace('\\', '/'));
        name = new string(name.Where(c => !char.IsControl(c)).ToArray()).Trim();
        if (name.Length == 0)
        {
            name = "file";
        }

        return name.Length > FileNameMaxLength ? name[..FileNameMaxLength] : name;
    }
}

public enum FileScanStatus
{
    PendingScan = 1,
    Clean = 2,
    Rejected = 3,
}
