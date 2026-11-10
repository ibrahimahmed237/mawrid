using Mawrid.Application.Common.Messaging;
using Mawrid.Application.Common.Persistence;
using Mawrid.Application.Common.Security;
using Mawrid.Application.Contracts.Files;
using Mawrid.Domain.Common;
using Mawrid.Domain.Files;
using Microsoft.EntityFrameworkCore;

namespace Mawrid.Application.Files;

public sealed record GetFileContentQuery(Guid FileId) : IQuery<FileContent>;

public sealed record FileContent(Stream Content, string ContentType, string FileName);

internal sealed class GetFileContentQueryHandler(IAppDbContext db, IFileStorage storage, ICurrentUser currentUser)
    : IQueryHandler<GetFileContentQuery, FileContent>
{
    public async Task<Result<FileContent>> Handle(GetFileContentQuery query, CancellationToken cancellationToken)
    {
        var file = await FileAccess.VisibleFiles(db, currentUser)
            .SingleOrDefaultAsync(f => f.Id == query.FileId, cancellationToken);
        if (file is null)
        {
            return FileErrors.NotFound;
        }

        var content = await storage.OpenReadAsync(file.StorageKey, cancellationToken);
        return content is null ? FileErrors.NotFound : new FileContent(content, file.ContentType, file.FileName);
    }
}

internal sealed class FileReferences(IAppDbContext db, ICurrentUser currentUser, IFileStorage storage) : IFileReferences
{
    public Task<bool> CanAttachAsync(Guid fileId, CancellationToken cancellationToken) =>
        FileAccess.VisibleFiles(db, currentUser).AnyAsync(f => f.Id == fileId, cancellationToken);

    public async Task<AttachedFile?> OpenAsync(Guid fileId, CancellationToken cancellationToken)
    {
        var file = await FileAccess.VisibleFiles(db, currentUser).SingleOrDefaultAsync(f => f.Id == fileId, cancellationToken);
        if (file is null)
        {
            return null;
        }

        var content = await storage.OpenReadAsync(file.StorageKey, cancellationToken);
        return content is null ? null : new AttachedFile(file.FileName, file.ContentType, content);
    }
}

internal static class FileAccess
{
    /// <summary>Clean files of this organization. Supplier users see only files uploaded by their own company.</summary>
    public static IQueryable<StoredFile> VisibleFiles(IAppDbContext db, ICurrentUser currentUser)
    {
        var files = db.Files.AsNoTracking().Where(f => f.ScanStatus == FileScanStatus.Clean);
        return currentUser.SupplierId is { } supplierId
            ? files.Where(f => f.UploaderSupplierId == supplierId)
            : files;
    }
}
