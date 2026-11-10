using Mawrid.Domain.Common;

namespace Mawrid.Application.Files;

public static class FileErrors
{
    public static readonly Error Empty = Error.Validation("file.empty", "The file is empty.");

    public static readonly Error TooLarge = Error.Validation("file.too_large", "The file is larger than 25 MB.");

    public static readonly Error TypeNotAllowed = Error.Validation(
        "file.type_not_allowed", $"Allowed file types: {string.Join(", ", FileTypes.Allowed)}.");

    public static readonly Error ContentMismatch = Error.Validation(
        "file.content_mismatch", "The file content does not match its type.");

    public static readonly Error Malware = Error.RuleViolation("file.malware_detected", "The file was rejected by the virus scan.");

    public static readonly Error NotFound = Error.NotFound("file.not_found", "The file was not found.");
}
