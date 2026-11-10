namespace Mawrid.Domain.Suppliers;

/// <summary>A document the supplier uploaded, such as its CR certificate, with an expiry date.</summary>
public sealed class SupplierDocument
{
    internal SupplierDocument(Guid id, SupplierDocumentType type, Guid fileId, DateOnly expiresOn, DateTimeOffset uploadedAtUtc)
    {
        Id = id;
        Type = type;
        FileId = fileId;
        ExpiresOn = expiresOn;
        UploadedAtUtc = uploadedAtUtc;
    }

    private SupplierDocument()
    {
    }

    public Guid Id { get; private set; }

    public SupplierDocumentType Type { get; private set; }

    /// <summary>The uploaded file in file storage.</summary>
    public Guid FileId { get; private set; }

    public DateOnly ExpiresOn { get; private set; }

    public DateTimeOffset UploadedAtUtc { get; private set; }

    public bool IsValidOn(DateOnly day) => ExpiresOn >= day;
}
