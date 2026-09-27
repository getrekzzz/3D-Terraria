using System.Text.Json;
using System.Text.Json.Serialization;

namespace Strata.Core.Persistence;

/// <summary>Lenient JSON options for save files (unknown members preserved via extension data).</summary>
public static class SaveJson
{
    public static readonly JsonSerializerOptions Options = Create();

    private static JsonSerializerOptions Create()
    {
        var o = new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            WriteIndented = true,
            AllowTrailingCommas = true,
            ReadCommentHandling = JsonCommentHandling.Skip,
        };
        // Converters in the options outrank [JsonConverter] on a type, so enums with their own reader (older names)
        // must be listed before the general enum converter.
        o.Converters.Add(new Strata.Core.World.EvilKindConverter());
        o.Converters.Add(new JsonStringEnumConverter(JsonNamingPolicy.CamelCase));
        return o;
    }
}
