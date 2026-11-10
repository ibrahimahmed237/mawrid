using System.ComponentModel.DataAnnotations;

namespace Mawrid.Infrastructure.Files;

public sealed class FileStorageOptions
{
    public const string SectionName = "FileStorage";

    /// <summary>Azure Storage connection string. Locally this points at Azurite.</summary>
    [Required]
    public string ConnectionString { get; init; } = string.Empty;

    [Required]
    [RegularExpression("^[a-z0-9-]{3,63}$")]
    public string ContainerName { get; init; } = "mawrid-files";
}
