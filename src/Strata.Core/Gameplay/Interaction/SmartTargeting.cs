using System.Numerics;
using Strata.Core.Common;
using Strata.Core.Content.Defs;
using Strata.Core.Gameplay.Items;
using Strata.Core.Gameplay.Mining;
using Strata.Core.Voxels;
using Strata.Core.Voxels.TileObjects;

namespace Strata.Core.Gameplay.Interaction;

/// <summary>
/// Terraria's Smart Cursor for tools (toggled with X): instead of the exact cell under the crosshair, a tool snaps to the
/// nearest thing near the aim it can work on. A pickaxe takes a block it can actually mine (ores and gems first, as
/// Terraria's smart cursor goes for shinies), an axe the nearest tree, a hammer an object only a hammer breaks.
/// <para>
/// The search is a cone round the aim line (12° half-angle, at most 0.75 m wide) sampled from the eye every 0.25 m up to
/// the reach, and only a little past the first solid cell on the aim line, so it never reaches through a wall. A block
/// counts only with a face open toward the eye (nothing buried is picked). Of the candidates the one closest along the
/// aim wins, the one nearer the line on a tie. Allocation-free and bounded (about 40 samples of at most 64 cells).
/// </para>
/// </summary>
public static class SmartTargeting
{
    /// <summary>How far apart (m) the cone is sampled along the aim.</summary>
    public const float StepMeters = 0.25f;

    /// <summary>The cone's widest radius (m) round the aim line.</summary>
    public const float MaxRadiusMeters = 0.75f;

    /// <summary>tan 12°: the cone's radius per metre along the aim.</summary>
    public const float ConeSlope = 0.21255656f;

    /// <summary>How far (m) past the first solid cell on the aim line the cone still looks (the cells round the hit).</summary>
    public const float PastSurfaceMeters = 0.75f;

    private static readonly Int3[] Faces =
    {
        new(1, 0, 0), new(-1, 0, 0), new(0, 1, 0), new(0, -1, 0), new(0, 0, 1), new(0, 0, -1),
    };

    /// <summary>A candidate: the cell, how far along the aim (cells) and off the line (cells) it is, and its face toward the eye.</summary>
    private struct Candidate
    {
        public bool Found;
        public Int3 Cell, Normal;
        public float Along, Lateral;
        public bool Shiny;
        public TileObjectInstance? Object;

        /// <summary>True when a candidate at (<paramref name="along"/>, <paramref name="lateral"/>) beats this one.</summary>
        public readonly bool Beaten(float along, float lateral, bool shiny = false)
        {
            if (!Found || (shiny && !Shiny)) return true;
            if (Shiny && !shiny) return false;
            return along < Along - 1e-4f || (along < Along + 1e-4f && lateral < Lateral);
        }
    }

    /// <summary>
    /// The smart target for <paramref name="info"/>'s tool from <paramref name="eye"/> (m) along <paramref name="aim"/>
    /// within <paramref name="reachM"/> metres (measured from the eye, as <see cref="ItemUseSystem.ReachMeters"/>), or
    /// null when the item is no tool or nothing in the cone suits it.
    /// </summary>
    public static Target? Find(VoxelWorld world, Vector3 eye, Vector3 aim, float reachM, ItemUseInfo info, int pickPower, int axePower, int hammerPower)
    {
        if (aim.LengthSquared() < 1e-10f || reachM <= 0) return null;
        bool mine = info.Has(ItemAction.Mine) && pickPower > 0;
        bool chop = info.Has(ItemAction.Chop) && axePower > 0;
        bool hammer = info.Has(ItemAction.Hammer) && hammerPower > 0;
        if (!mine && !chop && !hammer) return null;
        aim = Vector3.Normalize(aim);
        // The search runs in cell units.
        var origin = eye / Units.CellSize;
        float reach = reachM / Units.CellSize, step = StepMeters / Units.CellSize;
        float maxR = MaxRadiusMeters / Units.CellSize, past = PastSurfaceMeters / Units.CellSize;
        var block = default(Candidate);
        var obj = default(Candidate);
        float limit = reach;
        bool surfaced = false;
        Int3 prevMin = new(int.MaxValue, int.MaxValue, int.MaxValue), prevMax = new(int.MinValue, int.MinValue, int.MinValue);
        for (float t = step; t <= limit; t += step)
        {
            var p = origin + aim * t;
            var axisCell = CellAt(p);
            if (!surfaced && Solid(world, world.GetBlock(axisCell)))
            {
                surfaced = true;
                limit = MathF.Min(limit, t + past);
            }
            float r = MathF.Min(maxR, t * ConeSlope);
            var min = CellAt(p - new Vector3(r));
            var max = CellAt(p + new Vector3(r));
            for (int y = min.Y; y <= max.Y; y++)
                for (int z = min.Z; z <= max.Z; z++)
                    for (int x = min.X; x <= max.X; x++)
                    {
                        // Consecutive samples overlap: a cell the last box held was already weighed.
                        if (x >= prevMin.X && x <= prevMax.X && y >= prevMin.Y && y <= prevMax.Y && z >= prevMin.Z && z <= prevMax.Z) continue;
                        var cell = new Int3(x, y, z);
                        if (!InCone(cell, origin, aim, maxR, out float along, out float lateral)) continue;
                        ushort id = world.GetBlock(cell);
                        if (id == VoxelWorld.Unknown) continue;
                        if (mine && id != 0) ConsiderBlock(world, cell, id, origin, along, lateral, reachM, pickPower, ref block);
                        else if ((chop || hammer) && id == 0 && obj.Beaten(along, lateral) && world.TileObjects.At(cell) is { } o
                                 && Breaks(o.Def, chop ? axePower : 0, hammer ? hammerPower : 0)
                                 && NearestDistance(cell, origin) <= reachM)
                            obj = new Candidate { Found = true, Cell = cell, Normal = FaceToward(cell, origin), Along = along, Lateral = lateral, Object = o };
                    }
            prevMin = min;
            prevMax = max;
        }
        // An ore or gem in the cone wins; otherwise whichever of the block and the object is nearer along the aim.
        var best = block.Found && (block.Shiny || !obj.Found || block.Along <= obj.Along) ? block : obj;
        if (!best.Found) return null;
        var c = best.Cell;
        var hit = new Vector3(c.X + 0.5f, c.Y + 0.5f, c.Z + 0.5f) + new Vector3(best.Normal.X, best.Normal.Y, best.Normal.Z) * 0.5f;
        float distance = MathF.Min(Vector3.Distance(hit, origin) * Units.CellSize, reachM);
        return new Target(c, best.Normal, world.GetBlock(c), best.Object, hit, distance);
    }

    /// <summary>Weighs a block the pickaxe might take: minable, a face open toward the eye, and that face within reach.</summary>
    private static void ConsiderBlock(VoxelWorld world, Int3 cell, ushort id, Vector3 origin, float along, float lateral, float reachM, int pickPower, ref Candidate best)
    {
        var def = world.Content.Blocks[id];
        bool shiny = def.HasTag("ore") || def.HasTag("gem");
        if (!best.Beaten(along, lateral, shiny) || !MiningRules.CanMine(def, pickPower)) return;
        if (!OpenFace(world, cell, origin, out var normal)) return;
        var face = new Vector3(cell.X + 0.5f + normal.X * 0.5f, cell.Y + 0.5f + normal.Y * 0.5f, cell.Z + 0.5f + normal.Z * 0.5f);
        if (Vector3.Distance(face, origin) * Units.CellSize > reachM) return;
        best = new Candidate { Found = true, Cell = cell, Normal = normal, Along = along, Lateral = lateral, Shiny = shiny };
    }

    /// <summary>
    /// Whether the cell lies in the cone: <paramref name="along"/> is its centre's distance along the aim (cells) and
    /// <paramref name="lateral"/> how far its box stays from the aim line there (sampled at the centre's projection and
    /// half a cell either side, so a box the line grazes counts as on it).
    /// </summary>
    private static bool InCone(Int3 cell, Vector3 origin, Vector3 aim, float maxR, out float along, out float lateral)
    {
        var lo = new Vector3(cell.X, cell.Y, cell.Z);
        var hi = lo + Vector3.One;
        along = Vector3.Dot(lo + new Vector3(0.5f) - origin, aim);
        lateral = float.MaxValue;
        if (along <= 0) return false;
        for (int i = -1; i <= 1; i++)
        {
            var a = origin + aim * MathF.Max(0f, along + i * 0.5f);
            lateral = MathF.Min(lateral, Vector3.Distance(a, Vector3.Clamp(a, lo, hi)));
        }
        return lateral <= MathF.Min(maxR, along * ConeSlope);
    }

    /// <summary>The face of <paramref name="cell"/> most toward the eye whose neighbour is open (air or an unrendered block).</summary>
    private static bool OpenFace(VoxelWorld world, Int3 cell, Vector3 origin, out Int3 normal)
    {
        var toEye = origin - new Vector3(cell.X + 0.5f, cell.Y + 0.5f, cell.Z + 0.5f);
        normal = Int3.Zero;
        float best = 0f;
        foreach (var f in Faces)
        {
            float d = f.X * toEye.X + f.Y * toEye.Y + f.Z * toEye.Z;
            if (d <= best) continue;
            ushort n = world.GetBlock(cell + f);
            if (n == VoxelWorld.Unknown || Solid(world, n)) continue;
            best = d;
            normal = f;
        }
        return best > 0f;
    }

    /// <summary>The face of <paramref name="cell"/> most toward the eye (the dominant axis).</summary>
    private static Int3 FaceToward(Int3 cell, Vector3 origin)
    {
        var d = origin - new Vector3(cell.X + 0.5f, cell.Y + 0.5f, cell.Z + 0.5f);
        float ax = MathF.Abs(d.X), ay = MathF.Abs(d.Y), az = MathF.Abs(d.Z);
        if (ay >= ax && ay >= az) return new Int3(0, Math.Sign(d.Y), 0);
        return ax >= az ? new Int3(Math.Sign(d.X), 0, 0) : new Int3(0, 0, Math.Sign(d.Z));
    }

    /// <summary>How far (m) the eye is from the nearest point of the cell.</summary>
    private static float NearestDistance(Int3 cell, Vector3 origin)
    {
        var lo = new Vector3(cell.X, cell.Y, cell.Z);
        return Vector3.Distance(origin, Vector3.Clamp(origin, lo, lo + Vector3.One)) * Units.CellSize;
    }

    /// <summary>
    /// Objects the smart cursor takes for a tool: an axe's trees (and cacti and giant mushrooms), a hammer's hammer-only
    /// objects, each strong enough for its least power. Decor, furniture and pick-broken objects are left to the plain
    /// crosshair, so a tool never swings at a chest the aim merely passed.
    /// </summary>
    private static bool Breaks(TileObjectDef def, int axePower, int hammerPower) => def.BreakTool switch
    {
        BreakTool.Axe => axePower > 0 && axePower >= def.MinPower,
        BreakTool.Hammer => hammerPower > 0 && hammerPower >= def.MinPower,
        _ => false,
    } && !def.Decor;

    private static bool Solid(VoxelWorld world, ushort id) =>
        id != 0 && id != VoxelWorld.Unknown && world.Content.Blocks[id].Render != BlockRender.None;

    private static Int3 CellAt(Vector3 p) => new((int)MathF.Floor(p.X), (int)MathF.Floor(p.Y), (int)MathF.Floor(p.Z));
}
