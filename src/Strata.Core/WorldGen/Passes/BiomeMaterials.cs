using Strata.Core.Content;
using Strata.Core.Determinism;
using Strata.Core.WorldGen.Chunk;
using Strata.Core.WorldGen.Fields;

namespace Strata.Core.WorldGen.Passes;

/// <summary>Converts strata materials to each biome's palette, mixing materials in organic blobs.</summary>
internal sealed class BiomeMaterials
{
    private readonly NoiseField _mix;
    private readonly byte _tundra, _jungle, _desert, _undergroundDesert, _umbral, _sanguine, _mushroom, _ocean, _forest;

    public BiomeMaterials(SeedTree seeds, ContentDatabase content)
    {
        _mix = NoiseField.Create(seeds, "material_mix", 14f, NoiseFractal.Fbm, 2);
        byte Id(string b) => (byte)content.Biomes.Get(b).RuntimeId;
        _tundra = Id("tundra");
        _jungle = Id("jungle");
        _desert = Id("desert");
        _undergroundDesert = Id("underground_desert");
        _umbral = Id("corruption");
        _sanguine = Id("crimson");
        _mushroom = Id("mushroom");
        _ocean = Id("ocean");
        _forest = Id("forest");
    }

    public void Apply(ChunkGenContext ctx)
    {
        var p = ctx.Palette;
        var mix = new LatticeSampler(ctx.Origin, Common.Int3.One * ChunkGenContext.Size, (x, y, z) => _mix.Sample(x, y, z));
        int size = ChunkGenContext.Size;
        for (int lz = 0; lz < size; lz++)
            for (int lx = 0; lx < size; lx++)
            {
                int col = ChunkGenContext.Column(lx, lz);
                float h = ctx.SurfaceHeight[col];
                float soil = ctx.SoilDepth[col];
                int wx = ctx.Origin.X + lx, wz = ctx.Origin.Z + lz;
                float land = ctx.Plan.Landness.Sample(wx, wz);
                bool beach = land < 0.9f && h < ctx.Plan.Bands.SeaLevel + 5;
                for (int ly = 0; ly < size; ly++)
                {
                    int i = ChunkGenContext.Index(lx, ly, lz);
                    ushort b = ctx.Blocks[i];
                    if (b == p.Air || b == p.Bedrock || b == p.Ash) continue;
                    int wy = ctx.Origin.Y + ly;
                    float sd = ctx.SurfaceDepth(col, wy);
                    byte biome = ctx.Biome[i];
                    ushort result = b;
                    if (ctx.AboveRoofPocket(col, wy))
                    {
                        // Don't Dig Up: no loose sand or slush in the rock roof hanging over the bottom surface.
                        ctx.Blocks[i] = Roof(p, b, biome, sd, mix, wx, wy, wz);
                        continue;
                    }
                    if (biome == _forest || biome == _ocean)
                    {
                        if ((beach || biome == _ocean) && sd < soil + 4) result = p.Sand;
                    }
                    else if (biome == _tundra) result = Tundra(p, b, mix.Sample(wx, wy, wz));
                    else if (biome == _jungle) result = Jungle(p, b, mix.Sample(wx, wy, wz));
                    else if (biome == _desert) result = b == p.Loam || sd < soil + 6 ? p.Sand : sd < 32 ? p.Sandstone : b;
                    else if (biome == _undergroundDesert) result = Buried(p, mix.Sample(wx, wy, wz));
                    else if (biome == _umbral) result = b == p.Loam ? b : p.UmbralStone;
                    else if (biome == _sanguine) result = b == p.Loam ? b : p.SanguineStone;
                    else if (biome == _mushroom) result = p.Mud;
                    ctx.Blocks[i] = result;
                }
            }
    }

    private ushort Roof(BlockPalette p, ushort b, byte biome, float sd, LatticeSampler mix, int wx, int wy, int wz)
    {
        if (biome == _tundra) { ushort t = Tundra(p, b, mix.Sample(wx, wy, wz)); return t == p.Slush ? p.Ice : t; }
        if (biome == _jungle) return Jungle(p, b, mix.Sample(wx, wy, wz));
        if (biome == _desert) return sd < 32 ? p.Sandstone : b;
        if (biome == _undergroundDesert) { ushort u = Buried(p, mix.Sample(wx, wy, wz)); return u == p.Sand ? p.Sandstone : u; }
        if (biome == _umbral) return b == p.Loam ? b : p.UmbralStone;
        if (biome == _sanguine) return b == p.Loam ? b : p.SanguineStone;
        if (biome == _mushroom) return p.Mud;
        return b;
    }

    private static ushort Tundra(BlockPalette p, ushort b, float m)
    {
        if (b == p.Loam) return p.Snow;
        if (b == p.Stone) return m > 0.1f ? p.Ice : m < -0.35f ? p.Snow : p.Stone;
        return m > 0.05f ? p.Ice : m < -0.4f ? p.Slush : b;
    }

    private static ushort Jungle(BlockPalette p, ushort b, float m)
    {
        if (b == p.Loam) return p.Mud;
        return m > -0.12f ? p.Mud : b;
    }

    private static ushort Buried(BlockPalette p, float m) => m > 0.3f ? p.Sand : m > -0.15f ? p.Sandstone : p.PackedSand;
}
