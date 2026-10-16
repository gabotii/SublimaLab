using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace SublimaLab.Infrastructure.Persistence;

/// <summary>
/// Maps a value object to a JSON text column. Used for documents that are always read and
/// written as a whole (design documents, production specs, specifications) and whose shape
/// evolves with versioned schemas rather than table migrations.
/// </summary>
internal static class JsonColumn
{
    public static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web)
    {
        AllowOutOfOrderMetadataProperties = true,
        Converters = { new JsonStringEnumConverter() },
    };

    public static PropertyBuilder<T> HasJsonConversion<T>(this PropertyBuilder<T> builder)
        where T : class?
    {
        builder.HasConversion(
            value => Serialize(value),
            json => Deserialize<T>(json),
            new ValueComparer<T>(
                (left, right) => Serialize(left) == Serialize(right),
                value => Serialize(value).GetHashCode(StringComparison.Ordinal),
                value => Deserialize<T>(Serialize(value))));

        // Unbounded text: overrides the global string length convention (both providers support "text").
        builder.Metadata.SetMaxLength(null);
        builder.HasColumnType("text");
        return builder;
    }

    private static string Serialize<T>(T? value) => value is null ? "null" : JsonSerializer.Serialize(value, Options);

    private static T Deserialize<T>(string json) => JsonSerializer.Deserialize<T>(json, Options)!;
}
