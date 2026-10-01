using System.Collections.Generic;
using System.Text.Json;
using Godot;

namespace Strata.Game.Rendering;

/// <summary>
/// What the character wears, from the generated asset manifest (tools/blender/build_all.py): the armour pieces of every
/// set (model, bone, equipment slot, the fitted rig's bone length, whether it hides the hair), the fitted rig's bone
/// lengths, and which accessories are worn on the body (shields, balloons). Read once, on first use.
/// </summary>
public sealed class WornAssets
{
    public sealed record Piece(string Name, string Model, string Bone, string Slot, float RefLength, bool HidesHair);

    private static WornAssets? _instance;
    private static readonly Piece[] None = System.Array.Empty<Piece>();
    private readonly Dictionary<string, Piece[]> _sets = new();
    private readonly Dictionary<string, string> _worn = new();
    private readonly Dictionary<string, float> _rig = new();

    public static WornAssets Instance => _instance ??= new WornAssets();

    private WornAssets()
    {
        const string path = "res://assets/manifest.json";
        if (!FileAccess.FileExists(path)) return;
        try
        {
            using var doc = JsonDocument.Parse(FileAccess.GetFileAsString(path));
            Read(doc.RootElement);
        }
        catch (JsonException e)
        {
            GD.PushError($"assets/manifest.json: {e.Message} (worn armour and accessories are not drawn)");
        }
    }

    private void Read(JsonElement root)
    {
        if (root.TryGetProperty("armor", out var armor) && armor.ValueKind == JsonValueKind.Object)
            foreach (var set in armor.EnumerateObject())
            {
                var pieces = new List<Piece>();
                foreach (var p in set.Value.EnumerateObject())
                    if (Text(p.Value, "model") is { } model && Text(p.Value, "bone") is { } bone)
                        pieces.Add(new Piece(p.Name, model, bone, Text(p.Value, "slot") ?? "",
                            p.Value.TryGetProperty("refLength", out var r) && r.ValueKind == JsonValueKind.Number ? r.GetSingle() : 0f,
                            p.Value.TryGetProperty("hidesHair", out var h) && h.ValueKind == JsonValueKind.True));
                _sets[set.Name] = pieces.ToArray();
            }
        if (root.TryGetProperty("armorRig", out var rig) && rig.ValueKind == JsonValueKind.Object)
            foreach (var b in rig.EnumerateObject())
                if (b.Value.ValueKind == JsonValueKind.Number) _rig[b.Name] = b.Value.GetSingle();
        if (root.TryGetProperty("items", out var items) && items.ValueKind == JsonValueKind.Object)
            foreach (var item in items.EnumerateObject())
                if (Text(item.Value, "worn") is { } worn) _worn[item.Name] = worn;
    }

    private static string? Text(JsonElement e, string key) =>
        e.ValueKind == JsonValueKind.Object && e.TryGetProperty(key, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;

    /// <summary>The pieces of an armour set (or single piece such as the mining helmet); empty when it has none.</summary>
    public IReadOnlyList<Piece> Set(string key) => _sets.TryGetValue(key, out var p) ? p : None;

    /// <summary>"shield" or "balloon" for accessories drawn on the body, otherwise null.</summary>
    public string? Worn(string itemId) => _worn.TryGetValue(itemId, out var w) ? w : null;

    /// <summary>A bone's length on the rig the armour was fitted to (0 when unknown).</summary>
    public float RigLength(string bone) => _rig.TryGetValue(bone, out var l) ? l : 0f;
}
