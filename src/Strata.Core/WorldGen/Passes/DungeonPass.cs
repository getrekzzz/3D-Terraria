using Strata.Core.Common;
using Strata.Core.Determinism;
using Strata.Core.World;
using Strata.Core.WorldGen.Chunk;
using Strata.Core.WorldGen.Structures;

namespace Strata.Core.WorldGen.Passes;

/// <summary>
/// Pass 10 — the Old Crypt, a sprawling procedural fortress near the coast on the tundra side of the
/// world, registered as the "dungeon" zone (its own spawns and music).
/// </summary>
public sealed class DungeonPass : GenPass
{
    public override string Name => "dungeon";
    public override int Number => 10;

    public override void Plan(PlanContext ctx)
    {
        var rng = ctx.SeedsFor(this).Rng("layout");
        float angle = ctx.Plan.SnowAngle + (rng.Chance(0.5f) ? 0.07f : -0.07f);
        float dist = ctx.LandRadius * 0.72f;
        float x = ctx.CenterX + Trig.Cos(angle) * dist, z = ctx.CenterZ + Trig.Sin(angle) * dist;
        for (int i = 0; i < 20 && ctx.Plan.Landness.Sample(x, z) < 0.97f; i++)
        {
            dist *= 0.95f;
            x = ctx.CenterX + Trig.Cos(angle) * dist;
            z = ctx.CenterZ + Trig.Sin(angle) * dist;
        }
        var gate = new Int3((int)x, (int)MathF.Floor(ctx.Terrain.SurfaceHeight(x, z)), (int)z);
        var (nx, ny, nz) = ctx.Plan.Settings.Size switch
        {
            WorldSizePreset.Tiny => (3, 2, 3),
            WorldSizePreset.Small => (5, 3, 5),
            WorldSizePreset.Medium => (6, 4, 6),
            _ => (7, 4, 7),
        };
        var bp = DungeonBuilder.Build(ctx.Content, gate, ref rng, nx, ny, nz);
        StructurePlacementPass.AddStructure(ctx.Plan, "dungeon", bp, zone: "dungeon");
    }

    public override void Generate(ChunkGenContext ctx)
    {
        foreach (var f in ctx.Plan.Features.InChunk(ctx.Coord))
            if (f is StructureFeature s && s.Kind == "dungeon") FeatureCarver.Stamp(ctx, s.Blueprint);
    }
}
