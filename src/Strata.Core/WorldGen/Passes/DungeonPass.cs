using Strata.Core.Common;
using Strata.Core.Determinism;
using Strata.Core.WorldGen.Chunk;
using Strata.Core.WorldGen.Plan;
using Strata.Core.WorldGen.Structures;
using Strata.Core.WorldGen.Structures.Dungeon;

namespace Strata.Core.WorldGen.Passes;

/// <summary>
/// Pass 10 — the Dungeon: a brick keep near the coast on the snow side of the world (the jungle is opposite) and the
/// winding halls, stairs, shafts and rooms the planner lays out under it, registered as the "dungeon" zone (its own
/// spawns, music and mining lock). Only samples the terrain's surface height and landness, so it stays independent of
/// how the relief is shaped.
/// </summary>
public sealed class DungeonPass : GenPass
{
    public override string Name => "dungeon";
    public override int Number => 10;

    public override void Plan(PlanContext ctx)
    {
        var seeds = ctx.SeedsFor(this);
        var rng = seeds.Rng("layout");
        var site = Site(ctx, ref rng);
        var layout = DungeonPlanner.Plan(site, DungeonBudget.For(ctx.Plan.Settings.Size), seeds);
        var styleRng = seeds.Rng("style");
        var style = DungeonStyle.Pick(ref styleRng);
        var bp = DungeonBuilder.Build(ctx.Content, layout, style, site.Surface);
        StructurePlacementPass.AddStructure(ctx.Plan, new DungeonFeature(bp, layout, style) { Kind = "dungeon", Zone = "dungeon" });

        var marks = ctx.Plan.Landmarks;
        var bottom = layout.VaultRoom >= 0 ? layout.Rooms[layout.VaultRoom] : Deepest(layout);
        marks[LandmarkIds.DungeonBottom] = bottom.CenterFloor;
        if (layout.ChestSpots.Count > 0)
        {
            var spot = layout.ChestSpots[0];
            var room = layout.Rooms[spot.Room].Interior;
            var front = spot.Cell + spot.Facing * 3;
            marks[LandmarkIds.DungeonChest] = new Int3(Math.Clamp(front.X, room.Min.X, room.Max.X - 1), room.Min.Y, Math.Clamp(front.Z, room.Min.Z, room.Max.Z - 1));
        }
        else marks[LandmarkIds.DungeonChest] = bottom.CenterFloor;
        marks[LandmarkIds.DungeonGate] = DungeonBuilder.GateCell(layout, site.Surface);
    }

    /// <summary>
    /// The snow-side angle ±0.07 at 0.86 of the land radius, stepping inward while the landness there is under 0.97;
    /// inward is the dominant axis toward the world centre.
    /// </summary>
    internal static DungeonSite Site(PlanContext ctx, ref Pcg32 rng)
    {
        var plan = ctx.Plan;
        float angle = plan.SnowAngle + (rng.Chance(0.5f) ? 0.07f : -0.07f);
        float dist = ctx.LandRadius * 0.86f;
        float x = ctx.CenterX + Trig.Cos(angle) * dist, z = ctx.CenterZ + Trig.Sin(angle) * dist;
        for (int i = 0; i < 20 && plan.Landness.Sample(x, z) < 0.97f; i++)
        {
            dist *= 0.95f;
            x = ctx.CenterX + Trig.Cos(angle) * dist;
            z = ctx.CenterZ + Trig.Sin(angle) * dist;
        }
        float dx = ctx.CenterX - x, dz = ctx.CenterZ - z;
        var inward = MathF.Abs(dx) >= MathF.Abs(dz) ? new Int3(Math.Sign(dx), 0, 0) : new Int3(0, 0, Math.Sign(dz));
        var terrain = ctx.Terrain;
        Func<int, int, int> surface = (sx, sz) => (int)MathF.Floor(terrain.SurfaceHeight(sx, sz));
        return new DungeonSite
        {
            Gate = new Int3((int)x, surface((int)x, (int)z), (int)z), Inward = inward, WorldSize = plan.Size, Bands = plan.Bands,
            Surface = surface, Landness = (sx, sz) => plan.Landness.Sample(sx, sz),
        };
    }

    private static DungeonRoom Deepest(DungeonLayout layout)
    {
        var best = layout.Rooms[0];
        foreach (var r in layout.Rooms) if (r.Interior.Min.Y < best.Interior.Min.Y) best = r;
        return best;
    }

    public override void Generate(ChunkGenContext ctx)
    {
        foreach (var f in ctx.Plan.Features.InChunk(ctx.Coord))
            if (f is DungeonFeature d)
            {
                // Objects earlier passes left where the dungeon now stands (a cabin's chest) would sit in its walls.
                ctx.Objects.RemoveAll(o => d.Blueprint.InZone(o.Anchor));
                FeatureCarver.Stamp(ctx, d.Blueprint);
            }
    }
}
