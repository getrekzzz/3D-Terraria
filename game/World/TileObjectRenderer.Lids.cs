using System.Collections.Generic;
using Godot;
using Strata.Core.Content;
using Strata.Core.Voxels.TileObjects;
using Strata.Game.Rendering;

namespace Strata.Game.World;

/// <summary>
/// Container lids: a model's <c>lid</c> part (exported hinged at the back top edge, tools/blender/bgen/furniture.py)
/// is drawn as a second MultiMesh beside its group's. A lid swings open while the container is shown and shut when it
/// closes; only the lids that are moving rewrite their instance, so a still world costs nothing per frame.
/// </summary>
public sealed partial class TileObjectRenderer
{
    private const string LidPartName = "lid";
    private const float LidOpenAngle = 100f * Mathf.Pi / 180f;
    private const float LidSeconds = 0.15f;

    /// <summary>The lids of one group: same instance order and capacity as the group's own buffer.</summary>
    private sealed class LidPart
    {
        public MultiMeshInstance3D Node = null!;
        public MultiMesh Mesh = null!;
        public float[] Buffer = System.Array.Empty<float>();
        public Transform3D Rest;
    }

    /// <summary>Open progress (0 shut … 1 open) of every lid not shut; the ones wanted open are in <see cref="_lidsWanted"/>.</summary>
    private readonly Dictionary<TileObjectInstance, float> _lidProgress = new();
    private readonly HashSet<TileObjectInstance> _lidsWanted = new();
    private readonly HashSet<TileObjectInstance> _lidsMoving = new();
    private readonly List<TileObjectInstance> _lidsSettled = new();

    /// <summary>Drawn lids that are open or still swinging (smoke probe <c>objects.open_lids</c>).</summary>
    public int OpenLids
    {
        get
        {
            int n = 0;
            foreach (var obj in _lidProgress.Keys)
                if (_chunks.TryGetValue(ChunkOf(obj), out var state) && state.Groups.TryGetValue((obj.Def, obj.Variant), out var g) && g.Lid != null) n++;
            return n;
        }
    }

    /// <summary>Swings a container's lid open (its contents are shown) or shut.</summary>
    public void SetLidOpen(TileObjectInstance obj, bool open)
    {
        if (open ? !_lidsWanted.Add(obj) : !_lidsWanted.Remove(obj)) return;
        _lidsMoving.Add(obj);
    }

    private void ForgetLid(TileObjectInstance obj)
    {
        _lidProgress.Remove(obj);
        _lidsWanted.Remove(obj);
        _lidsMoving.Remove(obj);
    }

    private void AnimateLids(double delta)
    {
        if (_lidsMoving.Count == 0) return;
        float step = (float)delta / LidSeconds;
        _lidsSettled.Clear();
        foreach (var obj in _lidsMoving)
        {
            _lidProgress.TryGetValue(obj, out var t);
            bool open = _lidsWanted.Contains(obj);
            t = Mathf.Clamp(t + (open ? step : -step), 0f, 1f);
            if (t > 0f) _lidProgress[obj] = t;
            else _lidProgress.Remove(obj);
            if (t == (open ? 1f : 0f)) _lidsSettled.Add(obj);
            WriteLid(obj);
        }
        foreach (var obj in _lidsSettled) _lidsMoving.Remove(obj);
    }

    /// <summary>Gives a new group its lid MultiMesh when the model has a lid part.</summary>
    private void AttachLid(Group g, TileObjectInstance o)
    {
        if (_props.Part(AssetPaths.ObjectModel(o.Def, o.Variant), LidPartName, multimesh: true) is not { } part) return;
        var mm = new MultiMesh { TransformFormat = MultiMesh.TransformFormatEnum.Transform3D, UseCustomData = true, Mesh = part.Mesh };
        var node = new MultiMeshInstance3D
        {
            Multimesh = mm,
            CastShadow = g.Node.CastShadow,
            Visible = g.Node.Visible,
            VisibilityRangeEnd = g.Node.VisibilityRangeEnd,
        };
        AddChild(node);
        g.Lid = new LidPart { Node = node, Mesh = mm, Rest = part.Hinge };
    }

    /// <summary>Every lid of a group after its own buffer was written (same capacity, light copied from it).</summary>
    private void WriteLids(Group g)
    {
        if (g.Lid is not { } lid) return;
        if (lid.Buffer.Length != g.Buffer.Length)
        {
            lid.Mesh.InstanceCount = g.Buffer.Length / Stride;
            lid.Buffer = new float[g.Buffer.Length];
        }
        for (int i = 0; i < g.Count; i++) WriteLidAt(g, lid, i);
        int used = g.Count * Stride;
        for (int o = used; g.Count > 0 && o < lid.Buffer.Length; o += Stride) System.Array.Copy(lid.Buffer, used - Stride, lid.Buffer, o, Stride);
        lid.Mesh.VisibleInstanceCount = g.Count;
        RenderingServer.MultimeshSetBuffer(lid.Mesh.GetRid(), lid.Buffer);
    }

    /// <summary>Light-only refresh of a group's lids (copied from the group's buffer).</summary>
    private static void WriteLidLight(Group g)
    {
        if (g.Lid is not { } lid || lid.Buffer.Length != g.Buffer.Length) return;
        for (int i = 0; i < g.Count; i++) System.Array.Copy(g.Buffer, i * Stride + 12, lid.Buffer, i * Stride + 12, 4);
        RenderingServer.MultimeshSetBuffer(lid.Mesh.GetRid(), lid.Buffer);
    }

    /// <summary>Rewrites one moving lid's instance, when its chunk is drawn.</summary>
    private void WriteLid(TileObjectInstance obj)
    {
        if (!_chunks.TryGetValue(ChunkOf(obj), out var state) || !state.Groups.TryGetValue((obj.Def, obj.Variant), out var g)) return;
        if (g.Lid is not { } lid || lid.Buffer.Length != g.Buffer.Length) return;
        int i = g.Objects.IndexOf(obj);
        if (i < 0 || i >= g.Count) return;
        WriteLidAt(g, lid, i);
        RenderingServer.MultimeshSetBuffer(lid.Mesh.GetRid(), lid.Buffer);
    }

    private void WriteLidAt(Group g, LidPart lid, int i)
    {
        var obj = g.Objects[i];
        _lidProgress.TryGetValue(obj, out var t);
        float eased = t * t * (3f - 2f * t);
        // Positive about the hinge's X lifts the front edge (the lid's -Z) up and back.
        var swing = new Transform3D(new Basis(Vector3.Right, eased * LidOpenAngle), Vector3.Zero);
        var tr = Placement.TransformOf(obj) * lid.Rest * swing;
        var b = lid.Buffer;
        int o = i * Stride;
        var basis = tr.Basis;
        b[o] = basis.X.X; b[o + 1] = basis.Y.X; b[o + 2] = basis.Z.X; b[o + 3] = tr.Origin.X;
        b[o + 4] = basis.X.Y; b[o + 5] = basis.Y.Y; b[o + 6] = basis.Z.Y; b[o + 7] = tr.Origin.Y;
        b[o + 8] = basis.X.Z; b[o + 9] = basis.Y.Z; b[o + 10] = basis.Z.Z; b[o + 11] = tr.Origin.Z;
        System.Array.Copy(g.Buffer, o + 12, b, o + 12, 4);
    }
}
