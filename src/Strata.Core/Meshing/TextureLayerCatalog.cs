using System.Text.Json;
using Strata.Core.Content.Loading;

namespace Strata.Core.Meshing;

/// <summary>Maps texture layer names to texture-array slice indices (from assets/textures/terrain/layers.json).</summary>
public sealed class TextureLayerCatalog
{
    public const string Path = "assets/textures/terrain/layers.json";

    private readonly Dictionary<string, int> _layers = new(StringComparer.Ordinal);

    public int Missing { get; }
    public int Count => _layers.Count;
    public IReadOnlyCollection<string> Names => _layers.Keys;

    private TextureLayerCatalog(IEnumerable<(string Name, int Index)> layers)
    {
        foreach (var (name, index) in layers) _layers[name] = index;
        Missing = _layers.TryGetValue("missing", out var m) ? m : 0;
    }

    public static TextureLayerCatalog Load(IDataSource data)
    {
        using var doc = JsonDocument.Parse(data.ReadText(Path));
        return new TextureLayerCatalog(doc.RootElement.GetProperty("layers").EnumerateArray()
            .Select(e => (e.GetProperty("name").GetString()!, e.GetProperty("index").GetInt32())));
    }

    /// <summary>Layer index for a name; unknown names map to the magenta "missing" layer.</summary>
    public int IndexOf(string name) => _layers.TryGetValue(name, out var i) ? i : Missing;

    public bool Contains(string name) => _layers.ContainsKey(name);
}
