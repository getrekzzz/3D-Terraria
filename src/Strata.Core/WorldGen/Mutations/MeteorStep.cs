using Strata.Core.Common;
using Strata.Core.Content;
using Strata.Core.Content.Specs;
using Strata.Core.Determinism;

namespace Strata.Core.WorldGen.Mutations;

/// <summary>
/// A meteorite strike: a crater blasted into the natural surface at a seeded spot between a fifth and two fifths of the
/// world's width from its middle, its bowl lined with meteorite. The crater is also a zone (meteor heads, music).
/// </summary>
public sealed class MeteorStep : IMutationStep, IZoneStep
{
    private readonly ushort _ore;
    private readonly float _radius;
    private readonly float _x, _y, _z;
    private readonly Aabb3i _bounds;

    public MeteorStep(WorldGenerator gen, ContentDatabase content, MeteorMutationParams p, ulong seed)
    {
        _ore = (ushort)content.Blocks.Get(p.Block).RuntimeId;
        _radius = p.Radius;
        Zone = p.Zone ?? "meteorite";
        var rng = new Pcg32(seed);
        var size = gen.Plan.Size;
        float reach = MathF.Min(size.X, size.Z) * rng.Range(0.2f, 0.4f);
        float bearing = rng.NextFloat();
        _x = Math.Clamp(size.X * 0.5f + Trig.Cos(bearing) * reach, 16f, size.X - 16f);
        _z = Math.Clamp(size.Z * 0.5f + Trig.Sin(bearing) * reach, 16f, size.Z - 16f);
        _y = gen.Terrain.SurfaceHeight(_x, _z);
        int r = (int)MathF.Ceiling(_radius) + 2;
        _bounds = new Aabb3i(new Int3((int)_x - r, (int)_y - r, (int)_z - r), new Int3((int)_x + r + 1, (int)_y + r + 1, (int)_z + r + 1));
    }

    public string Zone { get; }

    /// <summary>The crater's centre (cells), on the surface.</summary>
    public Int3 Center => new((int)_x, (int)_y, (int)_z);

    public bool InZone(Int3 cell)
    {
        float dx = cell.X - _x, dz = cell.Z - _z, dy = cell.Y - _y;
        return dx * dx + dz * dz <= _radius * _radius * 4f && dy > -_radius * 1.5f && dy < _radius * 2.5f;
    }

    public bool Affects(ushort block) => true;

    public void Apply(ref ChunkBuffer buf, Int3 chunk)
    {
        var origin = ChunkMath.ChunkOrigin(chunk);
        var box = new Aabb3i(origin, origin + new Int3(ChunkMath.Size, ChunkMath.Size, ChunkMath.Size));
        if (!box.Intersects(_bounds)) return;
        // A bowl: air above a sphere centred a little over the surface, meteorite in the shell beneath.
        float cy = _y + _radius * 0.35f;
        float inner = _radius * 0.72f;
        for (int y = Math.Max(_bounds.Min.Y, origin.Y); y < Math.Min(_bounds.Max.Y, origin.Y + ChunkMath.Size); y++)
            for (int z = Math.Max(_bounds.Min.Z, origin.Z); z < Math.Min(_bounds.Max.Z, origin.Z + ChunkMath.Size); z++)
                for (int x = Math.Max(_bounds.Min.X, origin.X); x < Math.Min(_bounds.Max.X, origin.X + ChunkMath.Size); x++)
                {
                    float dx = x + 0.5f - _x, dy = y + 0.5f - cy, dz = z + 0.5f - _z;
                    float d = MathF.Sqrt(dx * dx + dy * dy + dz * dz);
                    if (d > _radius) continue;
                    if (d <= inner || dy > 0) buf.Set(x, y, z, 0);
                    else if (buf.Get(x, y, z) != 0) buf.Set(x, y, z, _ore);
                }
    }
}
