using System.Collections.Generic;
using Godot;
using Strata.Core.Content.Specs;
using Strata.Core.Gameplay.Entities;

namespace Strata.Game.Entities;

public sealed partial class NpcRenderer
{
    /// <summary>One unit-tall vine cylinder shared by every stalk and tether, stretched per creature.</summary>
    private static readonly CylinderMesh TetherMesh = new() { TopRadius = 0.05f, BottomRadius = 0.09f, Height = 1f, RadialSegments = 6, Rings = 1 };

    /// <summary>One material per tether colour (vine green by default).</summary>
    private static readonly Dictionary<string, StandardMaterial3D> TetherMaterials = new();

    private readonly Stack<MeshInstance3D> _tetherPool = new();

    /// <summary>Rooted creatures (snatchers) and tethered followers with a tether colour (Plantera's hooks and tentacles).</summary>
    private static bool HasTether(Npc npc) =>
        npc.Def.Brain.Type == BrainSpecs.Lunger || npc.Leader != null && npc.Def.TetherColor != null;

    private MeshInstance3D AcquireTether(string? color)
    {
        string key = color ?? "";
        if (!TetherMaterials.TryGetValue(key, out var material))
            TetherMaterials[key] = material = new StandardMaterial3D
            {
                AlbedoColor = color != null ? Color.FromHtml(color) : new Color(0.25f, 0.45f, 0.15f),
                Roughness = 0.9f,
            };
        if (!_tetherPool.TryPop(out var tether))
        {
            tether = new MeshInstance3D { Mesh = TetherMesh };
            AddChild(tether);
        }
        tether.MaterialOverride = material;
        tether.Visible = true;
        return tether;
    }

    private void ReleaseTether(MeshInstance3D? tether)
    {
        if (tether == null) return;
        tether.Visible = false;
        _tetherPool.Push(tether);
    }

    /// <summary>
    /// A vine to the head: from the leader's centre for a tethered follower whose leader lives (a vine from Plantera to
    /// each hook and tentacle, drawn thicker), else from the creature's anchor (a snatcher's root).
    /// </summary>
    private static void UpdateStalk(Npc npc, View view)
    {
        bool led = npc.Leader is { Alive: true, Removed: false };
        var from = led ? npc.Leader!.Center : npc.Anchor;
        var root = new Vector3(from.X, from.Y, from.Z);
        var head = view.Shown + new Vector3(0, npc.HalfExtents.Y * (led ? 1f : 0.8f), 0);
        var span = head - root;
        float len = span.Length();
        view.Stalk!.Visible = len > 0.05f;
        if (len <= 0.05f) return;
        var up = span / len;
        var side = Mathf.Abs(up.Y) > 0.95f ? Vector3.Right : up.Cross(Vector3.Up).Normalized();
        float girth = led ? 2.5f : 1f;
        var basis = new Basis(side * girth, up * len, side.Cross(up) * girth); // columns: the cylinder's local axes
        view.Stalk.GlobalTransform = new Transform3D(basis, root + span * 0.5f);
    }
}
