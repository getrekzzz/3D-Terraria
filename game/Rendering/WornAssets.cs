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
        using var doc = JsonDocument.Parse(FileAccess.GetFileAsString(path));
        var root = doc.RootElement;
        if (root.TryGetProperty("armor", out var armor))
            foreach (var set in armor.EnumerateObject())
            {
                var pieces = new List<Piece>();
                foreach (var p in set.Value.EnumerateObject())
                    pieces.Add(new Piece(p.Name, p.Value.GetProperty("model").GetString()!, p.Value.GetProperty("bone").GetString()!,
                        p.Value.TryGetProperty("slot", out var slot) ? slot.GetString()! : "",
                        p.Value.GetProperty("refLength").GetSingle(), p.Value.TryGetProperty("hidesHair", out var h) && h.GetBoolean()));
                _sets[set.Name] = pieces.ToArray();
            }
        if (root.TryGetProperty("armorRig", out var rig))
            foreach (var b in rig.EnumerateObject()) _rig[b.Name] = b.Value.GetSingle();
        if (root.TryGetProperty("items", out var items))
            foreach (var item in items.EnumerateObject())
                if (item.Value.TryGetProperty("worn", out var worn)) _worn[item.Name] = worn.GetString()!;
    }

    /// <summary>The pieces of an armour set (or single piece such as the mining helmet); empty when it has none.</summary>
    public IReadOnlyList<Piece> Set(string key) => _sets.TryGetValue(key, out var p) ? p : None;

    /// <summary>"shield" or "balloon" for accessories drawn on the body, otherwise null.</summary>
    public string? Worn(string itemId) => _worn.TryGetValue(itemId, out var w) ? w : null;

    /// <summary>A bone's length on the rig the armour was fitted to (0 when unknown).</summary>
    public float RigLength(string bone) => _rig.TryGetValue(bone, out var l) ? l : 0f;
}
