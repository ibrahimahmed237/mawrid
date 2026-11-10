namespace Mawrid.Application.Contracts.Files;

/// <summary>
/// Published by the Files module for other modules: can the current user attach this file?
/// It must exist in the current organization, be clean, and (for supplier users) belong to their company.
/// </summary>
public interface IFileReferences
{
    Task<bool> CanAttachAsync(Guid fileId, CancellationToken cancellationToken);
}
