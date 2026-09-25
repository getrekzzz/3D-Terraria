using Strata.Core.Content;
using Strata.Core.Content.Defs;
using Strata.Core.WorldGen.Chunk;

namespace Strata.Core.WorldGen.Passes;

/// <summary>Which trees and plants grow where (biome × ground material), with densities.</summary>
internal sealed class DecorPalette
{
    public DecorPalette(ContentDatabase content)
    {
        byte B(string id) => (byte)content.Biomes.Get(id).RuntimeId;
        TileObjectDef T(string id) => content.TileObjects.Get(id);
        Forest = B("forest"); Tundra = B("tundra"); Jungle = B("jungle"); Desert = B("desert");
        Umbral = B("umbral"); Sanguine = B("sanguine"); Mushroom = B("mushroom"); Ocean = B("ocean");
        Oak = T("oak_tree"); Pine = T("pine_tree"); Palm = T("palm_tree"); Mahoe = T("mahoe_tree");
        Gloom = T("umbral_tree"); Cactus = T("cactus"); GiantGlowcap = T("giant_glowcap");
        Grass = T("grass_tuft"); Flower = T("flower"); Bush = T("bush"); Fern = T("fern");
        FieldMushroom = T("mushroom_plant"); Glowcap = T("glowcap_plant");
        Stalagmite = T("stalagmite"); Stalactite = T("stalactite"); Rubble = T("rubble");
    }

    public byte Forest { get; }
    public byte Tundra { get; }
    public byte Jungle { get; }
    public byte Desert { get; }
    public byte Umbral { get; }
    public byte Sanguine { get; }
    public byte Mushroom { get; }
    public byte Ocean { get; }

    public TileObjectDef Oak { get; }
    public TileObjectDef Pine { get; }
    public TileObjectDef Palm { get; }
    public TileObjectDef Mahoe { get; }
    public TileObjectDef Gloom { get; }
    public TileObjectDef Cactus { get; }
    public TileObjectDef GiantGlowcap { get; }
    public TileObjectDef Grass { get; }
    public TileObjectDef Flower { get; }
    public TileObjectDef Bush { get; }
    public TileObjectDef Fern { get; }
    public TileObjectDef FieldMushroom { get; }
    public TileObjectDef Glowcap { get; }
    public TileObjectDef Stalagmite { get; }
    public TileObjectDef Stalactite { get; }
    public TileObjectDef Rubble { get; }

    public (TileObjectDef? Tree, float Chance) TreeFor(ChunkGenContext ctx, ushort ground, byte biome, int x, int z)
    {
        var p = ctx.Palette;
        if (ground == p.Sand && ctx.Plan.Landness.Sample(x, z) < 0.93f) return (Palm, 0.35f);
        if (ground == p.Sand && biome == Desert) return (Cactus, 0.12f);
        if (ground == p.Turf && biome == Forest) return (Oak, 0.42f);
        if (ground == p.Snow) return (Pine, 0.3f);
        if (ground == p.JungleTurf) return (Mahoe, 0.62f);
        if (ground == p.UmbralTurf || ground == p.SanguineTurf) return (Gloom, 0.3f);
        return (null, 0f);
    }

    public TileObjectDef? FloorPlant(ChunkGenContext ctx, ushort ground, byte biome, float roll)
    {
        var p = ctx.Palette;
        if (ground == p.Turf)
            return roll < 0.09f ? Grass : roll < 0.12f ? Flower : roll < 0.13f ? Bush : roll < 0.135f ? FieldMushroom : null;
        if (ground == p.JungleTurf) return roll < 0.12f ? Fern : roll < 0.15f ? Grass : null;
        if (ground == p.MushroomTurf) return roll < 0.05f ? Glowcap : roll < 0.054f ? GiantGlowcap : null;
        if (ground == p.Snow && biome == Tundra) return roll < 0.02f ? Grass : null;
        if (ground == p.Stone || ground == p.Deepstone || ground == p.MossStone)
            return roll < 0.008f ? Stalagmite : roll < 0.011f ? Rubble : null;
        return null;
    }
}
