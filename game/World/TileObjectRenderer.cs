using System.Collections.Generic;
using Godot;
using Strata.Core.Common;
using Strata.Core.Content;
using Strata.Core.Content.Defs;
using Strata.Core.Session;
using Strata.Core.Voxels.TileObjects;
using Strata.Game.Rendering;

namespace Strata.Game.World;

/// <summary>
/// Draws tile objects (trees, furniture, stations, flora) with one MultiMesh per chunk and model variant.
/// Chunks are rebuilt when their objects change, a door toggles or their voxel light is recomputed.
/// </summary>
public sealed partial class TileObjectRenderer : Node3D
{
    /// <summary>Objects at most this tall (cells) fade out beyond <see cref="SmallObjectRange"/>.</summary>
    private const int SmallObjectHeight = 2;
    private const float SmallObjectRange = 48f;

    private readonly Dictionary<Int3, List<MultiMeshInstance3D>> _chunks = new();
    private readonly HashSet<Int3> _dirty = new();
    private GameWorld _game = null!;
    private PropLibrary _props = null!;

    public int InstanceCount { get; private set; }

    public void Initialize(WorldRuntime runtime, PropLibrary props)
    {
        _game = runtime.Game;
        _props = props;
        Name = "TileObjects";
        var store = _game.Session.World.TileObjects;
        store.Added += o => _dirty.Add(ChunkOf(o));
        store.Removed += o => _dirty.Add(ChunkOf(o));
        _game.Interaction.StateChanged += o => _dirty.Add(ChunkOf(o));
        runtime.Streamer.MeshReady += r => _dirty.Add(r.Coord);
        runtime.Streamer.ChunkRemoved += c =>
        {
            _dirty.Remove(c);
            Clear(c);
        };
    }

    public override void _Process(double delta)
    {
        int budget = 8;
        var done = new List<Int3>();
        foreach (var c in _dirty)
        {
            if (budget-- <= 0) break;
            Rebuild(c);
            done.Add(c);
        }
        foreach (var c in done) _dirty.Remove(c);
    }

    private static Int3 ChunkOf(TileObjectInstance o) => ChunkMath.CellToChunk(o.Anchor);

    private void Rebuild(Int3 chunk)
    {
        Clear(chunk);
        var objects = _game.Session.World.TileObjects.InChunk(chunk);
        if (objects.Count == 0 || !_game.Session.World.HasChunk(chunk)) return;
        var groups = new Dictionary<(TileObjectDef Def, int Variant), List<TileObjectInstance>>();
        foreach (var o in objects)
        {
            var key = (o.Def, o.Variant);
            if (!groups.TryGetValue(key, out var list)) groups[key] = list = new List<TileObjectInstance>();
            list.Add(o);
        }
        var nodes = new List<MultiMeshInstance3D>();
        var world = _game.Session.World;
        foreach (var ((def, variant), list) in groups)
        {
            var mesh = _props.Model(AssetPaths.ObjectModel(def, variant), multimesh: true);
            if (mesh == null) continue;
            var mm = new MultiMesh
            {
                TransformFormat = MultiMesh.TransformFormatEnum.Transform3D,
                UseCustomData = true,
                Mesh = mesh,
                InstanceCount = list.Count,
            };
            for (int i = 0; i < list.Count; i++)
            {
                var o = list[i];
                mm.SetInstanceTransform(i, TransformOf(o));
                var fp = o.Footprint;
                var probe = new Vector3(o.Anchor.X + fp.X * 0.5f, o.Anchor.Y + 0.5f, o.Anchor.Z + fp.Z * 0.5f) * Units.CellSize;
                mm.SetInstanceCustomData(i, LightProbe.Sample(world, probe));
            }
            var node = new MultiMeshInstance3D { Multimesh = mm, CastShadow = def.SizeY <= SmallObjectHeight ? GeometryInstance3D.ShadowCastingSetting.Off : GeometryInstance3D.ShadowCastingSetting.On };
            if (def.SizeY <= SmallObjectHeight) node.VisibilityRangeEnd = SmallObjectRange;
            AddChild(node);
            nodes.Add(node);
            InstanceCount += list.Count;
        }
        _chunks[chunk] = nodes;
    }

    /// <summary>World transform of an object's model: footprint centre, quarter-turn yaw, doors swung open.</summary>
    public static Transform3D TransformOf(TileObjectInstance o)
    {
        var fp = o.Footprint;
        var origin = new Vector3(o.Anchor.X + fp.X * 0.5f, o.Anchor.Y, o.Anchor.Z + fp.Z * 0.5f) * Units.CellSize;
        var basis = new Basis(Vector3.Up, -o.Rotation * Mathf.Pi / 2f);
        if (o.Def.Interaction == TileInteraction.Door && o.Active)
        {
            // Swing about the hinge on the leaf's local -X edge.
            var hinge = new Vector3(-o.Def.SizeX * 0.5f * Units.CellSize, 0, 0);
            var swing = new Transform3D(Basis.Identity, hinge) * new Transform3D(new Basis(Vector3.Up, -Mathf.Pi / 2f), Vector3.Zero) * new Transform3D(Basis.Identity, -hinge);
            return new Transform3D(basis, origin) * swing;
        }
        return new Transform3D(basis, origin);
    }

    private void Clear(Int3 chunk)
    {
        if (!_chunks.Remove(chunk, out var nodes)) return;
        foreach (var n in nodes)
        {
            InstanceCount -= n.Multimesh.InstanceCount;
            n.QueueFree();
        }
    }
}
