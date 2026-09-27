using Strata.Core.Common;

namespace Strata.Core.World;

public enum WorldSizePreset
{
    /// <summary>Test/debug size.</summary>
    Tiny,
    Small,
    Medium,
    Large,
}

public enum Difficulty
{
    Classic,
    Expert,
    Master,
    Journey,
}

/// <summary>Which evil biome the world generates with (the other appears only via mutation/items).</summary>
[System.Text.Json.Serialization.JsonConverter(typeof(EvilKindConverter))]
public enum EvilKind
{
    Corruption,
    Crimson,
}

/// <summary>Reads evil kinds by name, including the names saves used before the rename (umbral, sanguine).</summary>
public sealed class EvilKindConverter : System.Text.Json.Serialization.JsonConverter<EvilKind>
{
    public override EvilKind Read(ref System.Text.Json.Utf8JsonReader reader, Type typeToConvert, System.Text.Json.JsonSerializerOptions options)
    {
        if (reader.TokenType == System.Text.Json.JsonTokenType.Number) return (EvilKind)reader.GetInt32();
        return reader.GetString()?.ToLowerInvariant() switch
        {
            "crimson" or "sanguine" => EvilKind.Crimson,
            _ => EvilKind.Corruption,
        };
    }

    public override void Write(System.Text.Json.Utf8JsonWriter writer, EvilKind value, System.Text.Json.JsonSerializerOptions options) =>
        writer.WriteStringValue(value == EvilKind.Crimson ? "crimson" : "corruption");
}

/// <summary>Immutable parameters chosen when a world is created.</summary>
public sealed class WorldSettings
{
    public string Name { get; init; } = "New World";
    public long Seed { get; init; }
    public WorldSizePreset Size { get; init; } = WorldSizePreset.Small;
    public Difficulty Difficulty { get; init; } = Difficulty.Classic;
    public EvilKind Evil { get; init; } = EvilKind.Corruption;

    public Int3 Dimensions => DimensionsOf(Size);

    /// <summary>World extent in cells. Multiples of the chunk size.</summary>
    public static Int3 DimensionsOf(WorldSizePreset size) => size switch
    {
        WorldSizePreset.Tiny => new Int3(512, 512, 512),
        WorldSizePreset.Small => new Int3(2048, 768, 2048),
        WorldSizePreset.Medium => new Int3(4096, 1024, 4096),
        _ => new Int3(6144, 1024, 6144),
    };

    public Int3 ChunkCounts => new(Dimensions.X / ChunkMath.Size, Dimensions.Y / ChunkMath.Size, Dimensions.Z / ChunkMath.Size);
}
