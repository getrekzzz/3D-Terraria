using System.Text.Json;
using System.Text.Json.Serialization;

namespace Strata.Core.Content;

/// <summary>Shared strict JSON settings for all content files.</summary>
public static class ContentJson
{
    public static readonly JsonSerializerOptions Options = Create();

    private static JsonSerializerOptions Create()
    {
        var o = new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            ReadCommentHandling = JsonCommentHandling.Skip,
            AllowTrailingCommas = true,
            UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
            WriteIndented = true,
        };
        o.Converters.Add(new JsonStringEnumConverter(JsonNamingPolicy.CamelCase, allowIntegerValues: false));
        return o;
    }
}
