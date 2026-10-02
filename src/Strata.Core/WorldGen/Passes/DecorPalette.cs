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
        Umbral = B("corruption"); Sanguine = B("crimson"); Mushroom = B("mushroom"); Ocean = B("ocean");
        Oak = T("oak_tree"); Pine = T("pine_tree"); Palm = T("palm_tree"); Mahoe = T("mahogany_tree");
        Gloom = T("ebonwood_tree"); Cactus = T("cactus"); GiantGlowcap = T("giant_glowing_mushroom");
        Grass = T("grass_tuft"); Flower = T("flower"); Bush = T("bush"); Fern = T("fern");
        FieldMushroom = T("mushroom_plant"); Glowcap = T("glowing_mushroom_plant");
        Stalagmite = T("stalagmite"); Stalactite = T("stalactite"); Rubble = T("rubble");
        TileObjectDef? O(string id) => content.TileObjects.TryGet(id, out var def) ? def : null;
        Daybloom = O("daybloom_plant"); Moonglow = O("moonglow_plant"); Blinkroot = O("blinkroot_plant"); Deathweed = O("deathweed_plant");
        Shade = O("shadewood_tree"); LifeCrystal = O("life_crystal_rock");
        StrangePlants = new[] { O("strange_plant_1_plant"), O("strange_plant_2_plant"), O("strange_plant_3_plant"), O("strange_plant_4_plant") }
            .Where(o => o != null).Select(o => o!).ToArray();
        Waterleaf = O("waterleaf_plant"); Fireblossom = O("fireblossom_plant"); Shiverthorn = O("shiverthorn_plant"); LifeFruit = O("life_fruit_plant");
    }

    // Alchemy herbs (optional content) and the jungle's Life Fruit.
    public TileObjectDef? Daybloom { get; }
    public TileObjectDef? Moonglow { get; }
    public TileObjectDef? Blinkroot { get; }
    public TileObjectDef? Deathweed { get; }
    public TileObjectDef? Waterleaf { get; }
    public TileObjectDef? Fireblossom { get; }
    public TileObjectDef? Shiverthorn { get; }
    public TileObjectDef? LifeFruit { get; }
    public TileObjectDef? Shade { get; }
    public TileObjectDef? LifeCrystal { get; }

    /// <summary>The four wild Strange Plants (the Dye Trader's trade; optional content, empty without it).</summary>
    public TileObjectDef[] StrangePlants { get; }

    /// <summary>Chance per open grass top (surface turf or jungle turf) of a Strange Plant: about one per 2000.</summary>
    public const float StrangePlantChance = 1f / 2000f;

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
        if (ground == p.Sand && ctx.Plan.Landness.Sample(x, z) < 0.93f) return (Palm, 0.4f);
        if (ground == p.Sand && biome == Desert) return (Cactus, 0.15f);
        if (ground == p.Turf && biome == Forest) return (Oak, 0.35f);
        if (ground == p.Snow) return (Pine, 0.3f);
        if (ground == p.JungleTurf) return (Mahoe, 0.75f);
        if (ground == p.UmbralTurf) return (Gloom, 0.3f);
        if (ground == p.SanguineTurf) return (Shade ?? Gloom, 0.3f);
        return (null, 0f);
    }

    public TileObjectDef? FloorPlant(ChunkGenContext ctx, ushort ground, byte biome, float roll, float depth)
    {
        var p = ctx.Palette;
        if (ground == p.Turf)
            return roll < 0.09f ? Grass : roll < 0.12f ? Flower : roll < 0.13f ? Bush : roll < 0.135f ? FieldMushroom : roll < 0.139f ? Daybloom : null;
        if (ground == p.JungleTurf) return roll < 0.12f ? Fern : roll < 0.15f ? Grass : roll < 0.155f ? Moonglow : null;
        if (ground == p.MushroomTurf) return roll < 0.05f ? Glowcap : roll < 0.054f ? GiantGlowcap : null;
        if (ground == p.Snow && biome == Tundra) return roll < 0.02f ? Grass : roll < 0.024f ? Shiverthorn : null;
        if (ground == p.Sand && biome == Desert) return roll < 0.004f ? Waterleaf : null;
        if (ground == p.UmbralTurf || ground == p.SanguineTurf) return roll < 0.01f ? Deathweed : null;
        if (ground == p.Ash) return roll < 0.006f ? Fireblossom : null;
        if (ground == p.Loam) return roll < 0.004f ? Blinkroot : null;
        if (ground == p.Mud) return roll < 0.004f ? Blinkroot : roll < 0.0055f && biome == Jungle ? LifeFruit : null;
        if (ground == p.Stone || ground == p.Deepstone || ground == p.MossStone)
            return roll < 0.008f ? Stalagmite : roll < 0.011f ? Rubble : roll < 0.01112f && depth > 0.3f ? LifeCrystal : null;
        return null;
    }
}
