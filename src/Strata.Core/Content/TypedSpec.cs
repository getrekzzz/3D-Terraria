using System.Text.Json;
using System.Text.Json.Serialization;

namespace Strata.Core.Content;

/// <summary>
/// An open-ended, type-tagged parameter object from data, e.g. <c>{ "type": "slimeHop", "jumpHeight": 6 }</c>.
/// Used for item behaviors, AI brains, conditions, buff/accessory effects and projectile hit effects.
/// The raw JSON is kept so systems resolve it through a registry into typed parameters.
/// </summary>
[JsonConverter(typeof(TypedSpecConverter))]
public sealed class TypedSpec
{
    public TypedSpec(string type, JsonElement raw)
    {
        Type = type;
        Raw = raw;
    }

    public string Type { get; }

    /// <summary>The whole JSON object including the "type" property.</summary>
    public JsonElement Raw { get; }

    public T Parse<T>() where T : SpecParams => Raw.Deserialize<T>(ContentJson.Options)
        ?? throw new JsonException($"Spec '{Type}' deserialized to null.");

    public static TypedSpec Of(string type, object? parameters = null)
    {
        var dict = parameters == null
            ? new Dictionary<string, object?>()
            : JsonSerializer.SerializeToElement(parameters, ContentJson.Options).Deserialize<Dictionary<string, object?>>()!;
        dict["type"] = type;
        return new TypedSpec(type, JsonSerializer.SerializeToElement(dict, ContentJson.Options));
    }

    public override string ToString() => Raw.GetRawText();
}

/// <summary>Base class for typed spec parameters (carries the discriminator so strict parsing accepts it).</summary>
public abstract class SpecParams
{
    [JsonRequired]
    public string Type { get; init; } = "";
}

internal sealed class TypedSpecConverter : JsonConverter<TypedSpec>
{
    public override TypedSpec Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        using var doc = JsonDocument.ParseValue(ref reader);
        var root = doc.RootElement;
        if (root.ValueKind != JsonValueKind.Object) throw new JsonException("Spec must be a JSON object.");
        if (!root.TryGetProperty("type", out var typeProp) || typeProp.ValueKind != JsonValueKind.String)
            throw new JsonException("Spec is missing a string \"type\" property.");
        return new TypedSpec(typeProp.GetString()!, root.Clone());
    }

    public override void Write(Utf8JsonWriter writer, TypedSpec value, JsonSerializerOptions options) =>
        value.Raw.WriteTo(writer);
}
