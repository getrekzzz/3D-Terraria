using System.Collections.Generic;
using Godot;
using Strata.Core.Common;
using Strata.Core.Content;
using Strata.Core.Content.Defs;
using Strata.Core.Voxels.TileObjects;
using Strata.Game.Rendering;

namespace Strata.Game.World;

/// <summary>Per-chunk object groups updated in place: one MultiMesh buffer write per group and update.</summary>
public sealed partial class TileObjectRenderer
{
    /// <summary>Objects at most this tall (cells) fade out beyond <see cref="SmallObjectRange"/>.</summary>
    private const int SmallObjectHeight = 2;
    private const float SmallObjectRange = 48f;

    /// <summary>Floats per instance in a MultiMesh buffer: a 3×4 transform plus custom data (the voxel light).</summary>
    private const int Stride = 16;

    /// <summary>One model variant's instances in a chunk, with its node, buffer and flames kept between updates.</summary>
    private sealed class Group
    {
        public MultiMeshInstance3D Node = null!;
        public MultiMesh Mesh = null!;
        public float[] Buffer = System.Array.Empty<float>();
        public readonly List<TileObjectInstance> Objects = new();
        public int Count;
        public bool Seen;
        public MultiMeshInstance3D? FlameNode;
        public readonly List<Vector3> FlameBases = new();
        public LidPart? Lid;
    }

    private sealed class ChunkObjects
    {
        public readonly Dictionary<(TileObjectDef Def, int Variant), Group> Groups = new();
    }

    private readonly Dictionary<Int3, ChunkObjects> _chunks = new();
    private readonly List<(TileObjectDef, int)> _stale = new();
    private readonly List<Vector3> _bases = new();

    private void Update(Int3 chunk, DirtyKind kind)
    {
        var objects = _game.Session.World.TileObjects.InChunk(chunk);
        // Only chunks with terrain on screen: objects of load-only border chunks would float without their ground.
        if (objects.Count == 0 || !_game.Session.World.HasChunk(chunk) || !_streamer.IsMeshed(chunk))
        {
            Clear(chunk);
            return;
        }
        bool known = _chunks.TryGetValue(chunk, out var state);
        if (kind == DirtyKind.Light && known)
        {
            using var light = Strata.Core.Diagnostics.Metrics.Frame.Time("gd.objects.light");
            foreach (var g in state!.Groups.Values) WriteLight(g);
            LightRefreshes++;
            return;
        }
        using var profile = Strata.Core.Diagnostics.Metrics.Frame.Time("gd.objects.rebuild");
        Rebuilds++;
        if (!known) _chunks[chunk] = state = new ChunkObjects();
        if (!known || (kind & DirtyKind.Structure) != 0) Regroup(chunk, state!, objects);
        bool visible = _chunkRenderer?.IsShown(chunk) ?? true;
        foreach (var g in state!.Groups.Values)
        {
            WriteAll(g);
            UpdateFlames(g, visible);
        }
    }

    /// <summary>Sorts the chunk's objects into groups, creating groups that appeared and freeing ones that vanished.</summary>
    private void Regroup(Int3 chunk, ChunkObjects state, IReadOnlyList<TileObjectInstance> objects)
    {
        foreach (var g in state.Groups.Values)
        {
            g.Seen = false;
            g.Objects.Clear();
        }
        bool visible = _chunkRenderer?.IsShown(chunk) ?? true;
        foreach (var o in objects)
        {
            var key = (o.Def, o.Variant);
            if (!state.Groups.TryGetValue(key, out var g))
            {
                var mesh = _props.Model(AssetPaths.ObjectModel(o.Def, o.Variant), multimesh: true);
                if (mesh == null) continue;
                state.Groups[key] = g = NewGroup(o.Def, mesh, visible);
                AttachLid(g, o);
            }
            g.Seen = true;
            g.Objects.Add(o);
        }
        _stale.Clear();
        foreach (var (key, g) in state.Groups)
            if (!g.Seen) _stale.Add(key);
        foreach (var key in _stale)
        {
            Free(state.Groups[key]);
            state.Groups.Remove(key);
        }
    }

    private Group NewGroup(TileObjectDef def, Mesh mesh, bool visible)
    {
        var mm = new MultiMesh
        {
            TransformFormat = MultiMesh.TransformFormatEnum.Transform3D,
            UseCustomData = true,
            Mesh = mesh,
        };
        bool small = def.SizeY <= SmallObjectHeight;
        var node = new MultiMeshInstance3D
        {
            Multimesh = mm,
            CastShadow = small ? GeometryInstance3D.ShadowCastingSetting.Off : GeometryInstance3D.ShadowCastingSetting.On,
            Visible = visible,
        };
        if (small) node.VisibilityRangeEnd = SmallObjectRange;
        AddChild(node);
        return new Group { Node = node, Mesh = mm };
    }

    /// <summary>Transforms and light of every instance, in one buffer upload (capacity grows by 1.5×).</summary>
    private void WriteAll(Group g)
    {
        int count = g.Objects.Count;
        InstanceCount += count - g.Count;
        g.Count = count;
        if (count * Stride > g.Buffer.Length)
        {
            int capacity = System.Math.Max(count, g.Buffer.Length / Stride * 3 / 2);
            g.Mesh.InstanceCount = capacity;
            g.Buffer = new float[capacity * Stride];
        }
        var b = g.Buffer;
        for (int i = 0; i < count; i++)
        {
            var t = Placement.TransformOf(g.Objects[i]);
            int o = i * Stride;
            var basis = t.Basis;
            b[o] = basis.X.X; b[o + 1] = basis.Y.X; b[o + 2] = basis.Z.X; b[o + 3] = t.Origin.X;
            b[o + 4] = basis.X.Y; b[o + 5] = basis.Y.Y; b[o + 6] = basis.Z.Y; b[o + 7] = t.Origin.Y;
            b[o + 8] = basis.X.Z; b[o + 9] = basis.Y.Z; b[o + 10] = basis.Z.Z; b[o + 11] = t.Origin.Z;
            WriteLightAt(b, o, g.Objects[i]);
        }
        // Spare capacity repeats the last instance so it never stretches the bounds; it is hidden anyway.
        int used = count * Stride;
        for (int o = used; count > 0 && o < b.Length; o += Stride) System.Array.Copy(b, used - Stride, b, o, Stride);
        g.Mesh.VisibleInstanceCount = count;
        RenderingServer.MultimeshSetBuffer(g.Mesh.GetRid(), b);
        WriteLids(g);
    }

    /// <summary>Light-only refresh: rewrites the instances' light in the existing buffer.</summary>
    private void WriteLight(Group g)
    {
        var b = g.Buffer;
        for (int i = 0; i < g.Count; i++) WriteLightAt(b, i * Stride, g.Objects[i]);
        RenderingServer.MultimeshSetBuffer(g.Mesh.GetRid(), b);
        WriteLidLight(g);
    }

    private void WriteLightAt(float[] b, int o, TileObjectInstance obj)
    {
        var fp = obj.Footprint;
        var probe = new Vector3(obj.Anchor.X + fp.X * 0.5f, obj.Anchor.Y + 0.5f, obj.Anchor.Z + fp.Z * 0.5f) * Units.CellSize;
        var c = LightProbe.Sample(_game.Session.World, probe);
        b[o + 12] = c.R; b[o + 13] = c.G; b[o + 14] = c.B; b[o + 15] = c.A;
    }

    /// <summary>Living flames over the lit objects of one kind (wired-off torches stay dark); rebuilt only when they move.</summary>
    private void UpdateFlames(Group g, bool visible)
    {
        if (g.Objects.Count == 0 || g.Objects[0].Def.Flame is not { } flame) return;
        _bases.Clear();
        foreach (var o in g.Objects)
        {
            if (o.Active && !o.IsDoor) continue;
            var t = Placement.TransformOf(o);
            _bases.Add(Flames.BaseOf(flame, t));
            foreach (var at in flame.Also) _bases.Add(t * new Vector3(at[0], at[1], at[2]));
        }
        if (Same(_bases, g.FlameBases)) return;
        FreeFlames(g);
        g.FlameBases.AddRange(_bases);
        if (_bases.Count == 0) return;
        var node = Flames.Batch(flame, _bases);
        node.Visible = visible;
        AddChild(node);
        g.FlameNode = node;
        InstanceCount += _bases.Count;
    }

    private static bool Same(List<Vector3> a, List<Vector3> b)
    {
        if (a.Count != b.Count) return false;
        for (int i = 0; i < a.Count; i++)
            if (a[i] != b[i]) return false;
        return true;
    }

    private void FreeFlames(Group g)
    {
        if (g.FlameNode != null)
        {
            InstanceCount -= g.FlameBases.Count;
            g.FlameNode.QueueFree();
            g.FlameNode = null;
        }
        g.FlameBases.Clear();
    }

    private void Free(Group g)
    {
        FreeFlames(g);
        InstanceCount -= g.Count;
        g.Count = 0;
        g.Node.QueueFree();
        g.Lid?.Node.QueueFree();
    }

    private static void SetVisible(ChunkObjects chunk, bool show)
    {
        foreach (var g in chunk.Groups.Values)
        {
            g.Node.Visible = show;
            if (g.Lid != null) g.Lid.Node.Visible = show;
            if (g.FlameNode != null) g.FlameNode.Visible = show;
        }
    }

    private void Clear(Int3 chunk)
    {
        if (!_chunks.Remove(chunk, out var state)) return;
        foreach (var g in state.Groups.Values) Free(g);
    }
}
