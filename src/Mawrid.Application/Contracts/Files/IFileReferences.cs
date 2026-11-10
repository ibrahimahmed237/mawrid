namespace Mawrid.Application.Contracts.Files;

/// <summary>
/// Published by the Files module for other modules: can the current user attach this file?
/// It must exist in the current organization, be clean, and (for supplier users) belong to their company.
/// </summary>
public interface IFileReferences
{
    Task<bool> CanAttachAsync(Guid fileId, CancellationToken cancellationToken);

    /// <summary>Opens a file the current user can attach, or returns null. The caller disposes the stream.</summary>
    Task<AttachedFile?> OpenAsync(Guid fileId, CancellationToken cancellationToken);
}

public sealed record AttachedFile(string FileName, string ContentType, Stream Content);
