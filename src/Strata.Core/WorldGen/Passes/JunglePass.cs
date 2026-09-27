using System.Numerics;
using Strata.Core.Determinism;
using Strata.Core.WorldGen.Chunk;
using Strata.Core.WorldGen.Features;

namespace Strata.Core.WorldGen.Passes;

/// <summary>
/// Pass 11 — jungle specifics: honey-filled hives buried in the jungle column (Queen Bee lairs) and the Jungle
/// Temple deep beneath the jungle's heart.
/// </summary>
public sealed class JunglePass : GenPass
{
    public override string Name => "jungle";
    public override int Number => 11;

    public override void Plan(PlanContext ctx)
    {
        var rng = ctx.SeedsFor(this).Rng("features");
        var bands = ctx.Plan.Bands;
        foreach (var jungle in ctx.Plan.Regions.Where(r => r.Biome == "jungle").ToList())
        {
            int hives = Math.Max(1, ctx.Scaled(2));
            for (int i = 0; i < hives; i++)
            {
                float a = rng.NextFloat(), d = rng.Range(0f, 0.55f) * jungle.RadiusX;
                var center = new Vector3(jungle.CenterX + Trig.Cos(a) * d, bands.YForNormalizedDepth(rng.Range(0.18f, 0.6f)), jungle.CenterZ + Trig.Sin(a) * d);
                float r = rng.Range(16f, 24f);
                ctx.Plan.AddFeature(new ChamberFeature(center, new Vector3(r, r * 0.8f, r)) { Kind = "hive", Chamber = ChamberKind.Hive, Zone = "hive", Seed = rng.NextUInt() });
            }

            // The Jungle Temple, deep under the jungle's heart (its door opens with the Temple Key from Plantera).
            var site = new Common.Int3((int)jungle.CenterX, (int)bands.YForNormalizedDepth(0.68f), (int)jungle.CenterZ);
            var templeRng = ctx.SeedsFor(this).Rng("temple");
            StructurePlacementPass.AddStructure(ctx.Plan, "jungle_temple", Structures.TempleBuilder.Build(ctx.Content, site, ref templeRng), zone: "jungle_temple");
        }
    }

    public override void Generate(ChunkGenContext ctx)
    {
        foreach (var f in ctx.Plan.Features.InChunk(ctx.Coord))
        {
            if (f is Structures.StructureFeature { Kind: "jungle_temple" } temple)
            {
                FeatureCarver.Stamp(ctx, temple.Blueprint);
                continue;
            }
            if (f is not ChamberFeature ch || ch.Kind != "hive") continue;
            MiniBiomeCarver.Carve(ctx, ch);
            // The Queen Bee's larva rests in the heart of the hive.
            var cell = new Common.Int3((int)MathF.Floor(ch.Center.X), (int)MathF.Floor(ch.Center.Y), (int)MathF.Floor(ch.Center.Z));
            if (Common.ChunkMath.CellToChunk(cell) == ctx.Coord)
                ctx.AddObject(new Voxels.TileObjects.TileObjectInstance(ctx.Gen.Content.TileObjects.Get("bee_larva"), cell, 0, 0));
        }
    }
}
