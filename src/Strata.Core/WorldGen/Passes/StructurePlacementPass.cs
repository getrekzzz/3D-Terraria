using System.Numerics;
using Strata.Core.Common;
using Strata.Core.Determinism;
using Strata.Core.WorldGen.Chunk;
using Strata.Core.WorldGen.Features;
using Strata.Core.WorldGen.Structures;

namespace Strata.Core.WorldGen.Passes;

/// <summary>
/// Pass 9 — structures and mini-biomes: abandoned cabins, mine networks, heartwood trees, desert
/// pyramids, blade shrines, and marble/granite cave pockets.
/// </summary>
public sealed class StructurePlacementPass : GenPass
{
    public override string Name => "structures";
    public override int Number => 9;

    public override void Plan(PlanContext ctx)
    {
        var rng = ctx.SeedsFor(this).Rng("placement");
        var plan = ctx.Plan;
        var bands = plan.Bands;
        var content = ctx.Content;

        PlaceCabins(ctx, ref rng);

        for (int i = 0, n = Math.Max(1, ctx.Scaled(5)); i < n; i++)
        {
            if (!ctx.TryRandomLandPoint(ref rng, 0.92f, 80f, out float x, out float z)) continue;
            int y = (int)bands.YForNormalizedDepth(rng.Range(0.05f, 0.3f));
            AddStructure(plan, "mineshaft", MineshaftBuilder.Build(content, new Int3((int)x, y, (int)z), ref rng, rng.Range(6, 12)));
        }

        for (int i = 0, n = Math.Max(1, ctx.Scaled(2)); i < n; i++)
        {
            if (!TryBiomeSurfacePoint(ctx, ref rng, "forest", 90f, out var ground)) continue;
            AddStructure(plan, "living_tree", LivingTreeBuilder.Build(content, ground, ref rng), zone: "living_tree");
        }

        if (rng.Chance(0.75f) && TryBiomeSurfacePoint(ctx, ref rng, "desert", 0f, out var dune))
            AddStructure(plan, "pyramid", PyramidBuilder.Build(content, dune, ref rng));

        for (int i = 0, n = Math.Max(1, ctx.Scaled(2)); i < n; i++)
        {
            if (!ctx.TryRandomLandPoint(ref rng, 0.9f, 120f, out float x, out float z)) continue;
            float y = bands.YForNormalizedDepth(rng.Range(0.1f, 0.4f));
            var center = new Vector3(x, y, z);
            plan.AddFeature(new ChamberFeature(center, new Vector3(7, 5, 7)) { Kind = "shrine", Chamber = ChamberKind.Shrine, Seed = rng.NextUInt() });
            var bp = new Blueprint { Flags = Voxels.CellFlags.None };
            var floor = new Int3((int)x - 1, (int)(y - 3), (int)z - 1);
            bp.Fill(new Aabb3i(floor - new Int3(5, 1, 5), floor + new Int3(8, 0, 8)), "green_moss");
            bp.Object(content.TileObjects.Get("enchanted_sword_shrine"), floor, 0);
            AddStructure(plan, "shrine_altar", bp);
        }

        ulong seed = ctx.SeedsFor(this).Value;
        for (int i = 0, n = ctx.Scaled(6); i < n; i++)
        {
            foreach (var kind in new[] { ChamberKind.Marble, ChamberKind.Granite })
            {
                if (!ctx.TryRandomLandPoint(ref rng, 0.9f, 60f, out float x, out float z)) continue;
                float y = bands.YForNormalizedDepth(rng.Range(0.3f, 0.85f));
                float r = rng.Range(14f, 24f);
                string zone = kind == ChamberKind.Marble ? "marble_cave" : "granite_cave";
                plan.AddFeature(new ChamberFeature(new Vector3(x, y, z), new Vector3(r, r * 0.6f, r * rng.Range(0.8f, 1.2f)))
                {
                    Kind = "minibiome", Chamber = kind, Zone = zone, Seed = seed + (ulong)(i * 2 + (int)kind),
                });
            }
        }
    }

    public override void Generate(ChunkGenContext ctx)
    {
        foreach (var f in ctx.Plan.Features.InChunk(ctx.Coord))
        {
            if (f is ChamberFeature ch && ch.Kind is "shrine" or "minibiome") MiniBiomeCarver.Carve(ctx, ch);
            else if (f is StructureFeature s && s.Kind is "cabin" or "mineshaft" or "living_tree" or "pyramid" or "shrine_altar")
                FeatureCarver.Stamp(ctx, s.Blueprint);
        }
    }

    /// <summary>
    /// Abandoned cabins, most of them standing in the cave tunnels (a worm tunnel or a level stretch of the cave
    /// network's passages runs through their doorways) so exploring finds them; the rest are buried somewhere in the rock
    /// as before. Cavern-layer cabins hold a Gold Chest.
    /// </summary>
    private static void PlaceCabins(PlanContext ctx, ref Pcg32 rng)
    {
        var plan = ctx.Plan;
        var bands = plan.Bands;
        // Tunnel segments well under the surface: the worm tunnels and the network's level passages from the cave pass,
        // as (floor point at the middle, direction).
        var tunnels = plan.Features.OfKind<CapsuleFeature>("worm")
            .Select(c => (At: (c.A + c.B) * 0.5f - new Vector3(0f, (c.RadiusA + c.RadiusB) * 0.5f - 1f, 0f), Along: c.B - c.A))
            .Concat(plan.Features.All.OfType<Caves.CavePassageFeature>()
                .Where(p => p.Slope <= 0.12f && p.Length >= 20f)
                .Select(p => (At: (p.A + p.B) * 0.5f, Along: p.B - p.A)))
            .Where(t => ctx.Terrain.SurfaceHeight(t.At.X, t.At.Z) - t.At.Y > 24f)
            .ToList();
        var placed = new List<Vector3>();
        for (int i = 0, n = ctx.Scaled(34); i < n; i++)
        {
            Vector3 at;
            Vector3? along = null;
            if (tunnels.Count > 0 && rng.Chance(0.8f))
            {
                var t = tunnels[rng.NextInt(tunnels.Count)];
                at = t.At;
                along = t.Along;
            }
            else
            {
                if (!ctx.TryRandomLandPoint(ref rng, 0.9f, 60f, out float x, out float z)) continue;
                at = new Vector3(x, bands.YForNormalizedDepth(rng.Range(0.06f, 0.85f)), z);
            }
            if (placed.Any(p => Vector3.DistanceSquared(p, at) < 40f * 40f)) continue; // keep cabins apart
            placed.Add(at);
            bool deep = bands.LayerAt(at.Y) is World.WorldLayer.Cavern or World.WorldLayer.Underworld;
            string wood = WoodFor(ctx, at.X, at.Y, at.Z);
            var floor = new Int3((int)at.X, (int)at.Y, (int)at.Z);
            AddStructure(plan, "cabin", CabinBuilder.Build(ctx.Content, floor, wood, ref rng, deep, along));
        }
    }

    internal static void AddStructure(Plan.WorldPlan plan, string kind, Blueprint bp, string? zone = null) =>
        plan.AddFeature(new StructureFeature(bp) { Kind = kind, Zone = zone });

    /// <summary>Adds a ready-made structure feature (e.g. the Dungeon, which carries its layout).</summary>
    internal static void AddStructure(Plan.WorldPlan plan, StructureFeature feature) => plan.AddFeature(feature);

    /// <summary>A surface point inside a given biome (landness high, not near spawn).</summary>
    internal static bool TryBiomeSurfacePoint(PlanContext ctx, ref Pcg32 rng, string biome, float minFromSpawn, out Int3 ground)
    {
        byte id = (byte)ctx.Content.Biomes.Get(biome).RuntimeId;
        for (int attempt = 0; attempt < 200; attempt++)
        {
            if (!ctx.TryRandomLandPoint(ref rng, 0.95f, minFromSpawn, out float x, out float z)) break;
            if (ctx.Biomes.SurfaceBiome(x, z) != id) continue;
            ground = new Int3((int)x, (int)MathF.Floor(ctx.Terrain.SurfaceHeight(x, z)), (int)z);
            return true;
        }
        ground = Int3.Zero;
        return false;
    }

    private static string WoodFor(PlanContext ctx, float x, float y, float z)
    {
        string biome = ctx.Content.Biomes[ctx.Biomes.BiomeAt(x, y, z)].Id;
        return biome switch { "tundra" => "boreal_wood", "jungle" => "rich_mahogany", _ => "wood" };
    }
}
