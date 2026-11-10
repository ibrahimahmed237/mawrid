using Azure;
using Azure.Storage.Blobs;
using Azure.Storage.Blobs.Models;
using Mawrid.Application.Files;
using Microsoft.Extensions.Options;

namespace Mawrid.Infrastructure.Files;

/// <summary>Stores file bytes in a private Azure Blob Storage container.</summary>
public sealed class AzureBlobFileStorage(IOptions<FileStorageOptions> options) : IFileStorage
{
    private readonly BlobContainerClient _container =
        new(options.Value.ConnectionString, options.Value.ContainerName);

    private bool _containerReady;

    public BlobContainerClient Container => _container;

    public async Task SaveAsync(string key, Stream content, string contentType, CancellationToken cancellationToken)
    {
        await EnsureContainerAsync(cancellationToken);
        await _container.GetBlobClient(key).UploadAsync(content, new BlobUploadOptions
        {
            HttpHeaders = new BlobHttpHeaders { ContentType = contentType },
        }, cancellationToken);
    }

    public async Task<Stream?> OpenReadAsync(string key, CancellationToken cancellationToken)
    {
        try
        {
            return await _container.GetBlobClient(key).OpenReadAsync(cancellationToken: cancellationToken);
        }
        catch (RequestFailedException ex) when (ex.Status == 404)
        {
            return null;
        }
    }

    private async Task EnsureContainerAsync(CancellationToken cancellationToken)
    {
        if (_containerReady)
        {
            return;
        }

        // Private container: no anonymous access. Files are served only through the API.
        await _container.CreateIfNotExistsAsync(PublicAccessType.None, cancellationToken: cancellationToken);
        _containerReady = true;
    }
}
