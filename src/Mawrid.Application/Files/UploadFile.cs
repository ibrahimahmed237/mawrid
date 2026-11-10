using System.Security.Cryptography;
using Mawrid.Application.Common.Abstractions;
using Mawrid.Application.Common.Messaging;
using Mawrid.Application.Common.Persistence;
using Mawrid.Application.Common.Security;
using Mawrid.Domain.Common;
using Mawrid.Domain.Files;

namespace Mawrid.Application.Files;

public sealed record UploadFileCommand(string FileName, string ContentType, Stream Content) : ICommand<FileDto>;

public sealed record FileDto(Guid Id, string FileName, string ContentType, long SizeBytes, string Sha256);

/// <summary>
/// Checks type, size and content, scans for malware, stores the bytes, and records the file.
/// A rejected file is recorded (for the audit trail) but its bytes are never stored.
/// </summary>
internal sealed class UploadFileCommandHandler(
    IAppDbContext db,
    IFileStorage storage,
    IMalwareScanner scanner,
    ICurrentUser currentUser,
    IClock clock) : ICommandHandler<UploadFileCommand, FileDto>
{
    public async Task<Result<FileDto>> Handle(UploadFileCommand command, CancellationToken cancellationToken)
    {
        var organizationId = currentUser.OrganizationId
            ?? throw new InvalidOperationException("Uploading a file needs an organization context.");

        if (!FileTypes.IsAllowed(command.ContentType))
        {
            return FileErrors.TypeNotAllowed;
        }

        // Copy into memory once (max 25 MB): we need the bytes for the checks, the hash and the upload.
        using var buffer = new MemoryStream();
        await CopyWithLimitAsync(command.Content, buffer, cancellationToken);
        if (buffer.Length == 0)
        {
            return FileErrors.Empty;
        }

        if (buffer.Length > FileTypes.MaxSizeBytes)
        {
            return FileErrors.TooLarge;
        }

        if (!FileTypes.MatchesSignature(command.ContentType, buffer.GetBuffer().AsSpan(0, (int)Math.Min(16, buffer.Length))))
        {
            return FileErrors.ContentMismatch;
        }

        var sha256 = Convert.ToHexStringLower(SHA256.HashData(buffer.GetBuffer().AsSpan(0, (int)buffer.Length)));
        var file = StoredFile.Create(organizationId, command.FileName, command.ContentType, buffer.Length, sha256,
            currentUser.UserId, currentUser.SupplierId, clock.UtcNow);

        buffer.Position = 0;
        var scan = await scanner.ScanAsync(buffer, cancellationToken);
        if (!scan.IsClean)
        {
            file.MarkRejected(scan.Threat ?? "Malware detected");
            db.Files.Add(file);
            await db.SaveChangesAsync(cancellationToken);
            return FileErrors.Malware;
        }

        buffer.Position = 0;
        await storage.SaveAsync(file.StorageKey, buffer, file.ContentType, cancellationToken);

        file.MarkClean();
        db.Files.Add(file);
        await db.SaveChangesAsync(cancellationToken);

        return new FileDto(file.Id, file.FileName, file.ContentType, file.SizeBytes, file.Sha256);
    }

    /// <summary>Copies at most one byte more than the limit, so a huge upload is not read to the end.</summary>
    private static async Task CopyWithLimitAsync(Stream source, Stream target, CancellationToken cancellationToken)
    {
        var chunk = new byte[81920];
        long total = 0;
        int read;
        while ((read = await source.ReadAsync(chunk, cancellationToken)) > 0)
        {
            await target.WriteAsync(chunk.AsMemory(0, read), cancellationToken);
            total += read;
            if (total > FileTypes.MaxSizeBytes)
            {
                return;
            }
        }
    }
}
