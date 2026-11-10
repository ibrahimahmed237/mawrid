using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Mawrid.Infrastructure.Persistence;

/// <summary>Stores a small list of records as one JSON column (enums by name).</summary>
internal static class JsonColumn
{
    private static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter() },
    };

    public static PropertyBuilder<List<T>> HasJsonConversion<T>(this PropertyBuilder<List<T>> property) =>
        property.HasConversion(
            list => JsonSerializer.Serialize(list, Options),
            json => JsonSerializer.Deserialize<List<T>>(json, Options) ?? new List<T>(),
            new ValueComparer<List<T>>(
                (a, b) => JsonSerializer.Serialize(a, Options) == JsonSerializer.Serialize(b, Options),
                list => JsonSerializer.Serialize(list, Options).GetHashCode(StringComparison.Ordinal),
                list => JsonSerializer.Deserialize<List<T>>(JsonSerializer.Serialize(list, Options), Options)!));
}
