using Strata.Core.Common;
using Strata.Core.Content;
using Strata.Core.Content.Defs;
using Strata.Core.Content.Specs;
using Strata.Core.Determinism;
using Strata.Core.WorldGen.Chunk;

namespace Strata.Core.WorldGen.Mutations;

/// <summary>
/// A slanted slab of the world converted through a conversion table (the radiant reach, a blight). It starts on the
/// surface at a seeded point, runs <see cref="WedgeMutationParams.Length"/> cells along a seeded direction and leans
/// sideways as it goes down to its depth. The slab is also the conversion's zone.
/// </summary>
public sealed class WedgeStep : IMutationStep, IZoneStep
{
    private const float AboveSurface = 12f;
    private const float Rough = 1.5f;

    private readonly WorldGenerator _gen;
    private readonly WedgeMutationParams _p;
    private readonly ushort[] _map;
    private readonly float _x0, _z0, _ux, _uz, _floorY;
    private readonly ulong _seed;

    public WedgeStep(WorldGenerator gen, ContentDatabase content, WedgeMutationParams p, ConversionDef conversion, ulong seed)
    {
        _gen = gen;
        _p = p;
        _seed = seed;
        Zone = conversion.Zone ?? conversion.Id;
        _map = new ushort[content.Blocks.Count];
        foreach (var (from, to) in conversion.Map) _map[content.Blocks.Get(from).RuntimeId] = (ushort)content.Blocks.Get(to).RuntimeId;
        var rng = new Pcg32(seed);
        var size = gen.Plan.Size;
        float land = MathF.Min(size.X, size.Z) * 0.42f;
        float bearing = rng.NextFloat(), heading = rng.NextFloat();
        _x0 = size.X * 0.5f + Trig.Cos(bearing) * land * p.Distance;
        _z0 = size.Z * 0.5f + Trig.Sin(bearing) * land * p.Distance;
        _ux = Trig.Cos(heading);
        _uz = Trig.Sin(heading);
        _floorY = gen.Plan.Bands.YForNormalizedDepth(p.Depth);
    }

    public string Zone { get; }

    /// <summary>Where the slab meets the surface (cells) and the direction it runs along.</summary>
    public (float X, float Z, float DirX, float DirZ) Anchor => (_x0, _z0, _ux, _uz);

    public bool Affects(ushort block) => _map[block] != 0;

    /// <summary>How far (cells) a column point is outside the slab's cross-section at a height (≤ 0 inside).</summary>
    private float Outside(float x, float y, float z, float surface)
    {
        float dx = x - _x0, dz = z - _z0;
        float along = dx * _ux + dz * _uz, across = -dx * _uz + dz * _ux;
        float depth = surface - y;
        if (depth < -AboveSurface || y < _floorY) return float.MaxValue;
        float lengthOut = MathF.Abs(along) - _p.Length * 0.5f;
        float widthOut = MathF.Abs(across - MathF.Max(0f, depth) * _p.Lean) - _p.HalfWidth;
        return MathF.Max(lengthOut, widthOut);
    }

    public bool InZone(Int3 cell) =>
        Outside(cell.X + 0.5f, cell.Y + 0.5f, cell.Z + 0.5f, _gen.Terrain.SurfaceHeight(cell.X, cell.Z)) <= 0f;

    public void Apply(ref ChunkBuffer buf, Int3 chunk)
    {
        var o = buf.Origin;
        int n = buf.Size;
        float half = n * 0.5f;
        // Quick reject: the chunk's centre column is far outside the slab.
        float cx = o.X + half, cz = o.Z + half;
        if (o.Y + n < _floorY) return;
        float centreSurface = _gen.Terrain.SurfaceHeight(cx, cz);
        if (o.Y > centreSurface + AboveSurface + n) return;
        float reach = half * 1.5f * (1f + _p.Lean) + Rough + 8f;
        if (Outside(cx, Math.Clamp(o.Y + half, _floorY, centreSurface), cz, centreSurface) > reach) return;
        for (int lz = 0; lz < n; lz++)
            for (int lx = 0; lx < n; lx++)
            {
                int x = o.X + lx, z = o.Z + lz;
                float surface = _gen.Terrain.SurfaceHeight(x, z);
                for (int ly = 0; ly < n; ly++)
                {
                    int y = o.Y + ly;
                    ushort block = buf.Get(x, y, z);
                    ushort to = _map[block];
                    if (to == 0) continue;
                    float rough = (Hash.ToUnitFloat(Hash.Cell(_seed, x, y, z)) - 0.5f) * 2f * Rough;
                    if (Outside(x + 0.5f, y + 0.5f, z + 0.5f, surface) <= rough) buf.Set(x, y, z, to);
                }
            }
    }
}
