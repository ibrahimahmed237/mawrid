using Mawrid.Api.Common;
using Mawrid.Api.Common.Errors;
using Mawrid.Application.Files;
using Microsoft.AspNetCore.Mvc;

namespace Mawrid.Api.Files;

[Route("api/v1/files")]
public sealed class FilesController : ApiControllerBase
{
    /// <summary>Uploads one file (PDF, PNG, JPEG or XML, at most 25 MB) as multipart form field "file".</summary>
    [HttpPost]
    [RequestSizeLimit(FileTypes.MaxSizeBytes + 1024 * 1024)]
    [ProducesResponseType<FileDto>(StatusCodes.Status201Created)]
    public async Task<IActionResult> Upload(IFormFile file, CancellationToken cancellationToken)
    {
        await using var stream = file.OpenReadStream();
        var result = await Sender.Send(new UploadFileCommand(file.FileName, file.ContentType, stream), cancellationToken);
        return result.IsSuccess
            ? Created($"/api/v1/files/{result.Value.Id}", result.Value)
            : result.Error!.ToActionResult();
    }

    [HttpGet("{id:guid}")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    public async Task<IActionResult> Download(Guid id, CancellationToken cancellationToken)
    {
        var result = await Sender.Send(new GetFileContentQuery(id), cancellationToken);
        return result.IsSuccess
            ? File(result.Value.Content, result.Value.ContentType, result.Value.FileName)
            : result.Error!.ToActionResult();
    }
}
