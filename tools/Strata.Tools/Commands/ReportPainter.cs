using System.Globalization;
using Strata.Core.Common;
using Strata.Core.Diagnostics;
using Strata.Core.Voxels;
using Strata.Core.WorldGen;
using Strata.Core.WorldGen.Chunk;
using Strata.Core.WorldGen.Features;
using Strata.Core.WorldGen.Structures;

namespace Strata.Tools.Commands;

/// <summary>Renders worldgen data into images.</summary>
internal sealed class ReportPainter
{
    private readonly WorldGenerator _gen;
    private readonly (byte R, byte G, byte B)[] _blockColors;

    public ReportPainter(WorldGenerator gen)
    {
        _gen = gen;
        _blockColors = gen.Content.Blocks.All.Select(b => Parse(b.Color)).ToArray();
    }

    public void BiomeMap(string path)
    {
        var size = _gen.Plan.Size;
        int scale = Math.Max(1, size.X / 512);
        int w = size.X / scale, h = size.Z / scale;
        var img = new Image(w, h);
        var biomeColors = _gen.Content.Biomes.All.Select(b => Parse(b.MapColor)).ToArray();
        int sea = _gen.Plan.Bands.SeaLevel;
        for (int pz = 0; pz < h; pz++)
            for (int px = 0; px < w; px++)
            {
                float x = px * scale, z = pz * scale;
                float height = _gen.Terrain.SurfaceHeight(x, z);
                var c = height < sea - 1 ? (30, 70, 140) : biomeColors[_gen.Biomes.SurfaceBiome(x, z)];
                float shade = 0.75f + 0.25f * Math.Clamp((height - sea) / 60f, -1f, 1f);
                img.Set(px, pz, c.Item1 * shade, c.Item2 * shade, c.Item3 * shade);
            }
        foreach (var f in _gen.Plan.Features.All)
        {
            var b = f.Bounds;
            int cx = (b.Min.X + b.Max.X) / 2 / scale, cz = (b.Min.Z + b.Max.Z) / 2 / scale;
            switch (f)
            {
                case StructureFeature s when s.Kind == "dungeon": img.Rect(b.Min.X / scale, b.Min.Z / scale, b.Max.X / scale, b.Max.Z / scale, 220, 40, 40); break;
                case StructureFeature s when s.Kind == "cabin": img.Dot(cx, cz, 1, 250, 220, 80); break;
                case StructureFeature s when s.Kind is "living_tree" or "pyramid" or "mineshaft": img.Dot(cx, cz, 3, 255, 140, 0); break;
                case IslandFeature: img.Dot(cx, cz, 4, 255, 255, 255); break;
                case ChamberFeature ch when ch.Chamber == ChamberKind.Grand: img.Dot(cx, cz, 5, 180, 60, 220); break;
                case ChamberFeature ch when ch.Chamber == ChamberKind.Hive: img.Dot(cx, cz, 3, 230, 170, 40); break;
            }
        }
        var spawn = _gen.Plan.Spawn;
        img.Dot(spawn.X / scale, spawn.Z / scale, 3, 255, 255, 255);
        img.Save(path);
    }

    public void ReliefMap(string path)
    {
        var size = _gen.Plan.Size;
        int scale = Math.Max(1, size.X / 512);
        int w = size.X / scale, h = size.Z / scale;
        var img = new Image(w, h);
        int sea = _gen.Plan.Bands.SeaLevel;
        for (int pz = 0; pz < h; pz++)
            for (int px = 0; px < w; px++)
            {
                float x = px * scale, z = pz * scale;
                float hh = _gen.Terrain.SurfaceHeight(x, z);
                float dx = _gen.Terrain.SurfaceHeight(x + scale, z) - hh;
                float light = Math.Clamp(0.6f - dx * 0.15f, 0.1f, 1f);
                if (hh < sea) img.Set(px, pz, 20, 50 + (hh - sea + 80), 120 + (hh - sea + 80));
                else img.Set(px, pz, 90 * light + (hh - sea), 110 * light + (hh - sea) * 0.8f, 70 * light + (hh - sea) * 0.6f);
            }
        img.Save(path);
    }

    public void Section(string path, Dictionary<Int3, GeneratedChunk> chunks, bool alongX, int fixedCoord)
    {
        var size = _gen.Plan.Size;
        int w = alongX ? size.X : size.Z, h = size.Y;
        var img = new Image(w, h);
        int local = fixedCoord & ChunkMath.Mask;
        for (int a = 0; a < w; a++)
            for (int y = 0; y < h; y++)
            {
                var chunkCoord = alongX ? new Int3(a >> 5, y >> 5, fixedCoord >> 5) : new Int3(fixedCoord >> 5, y >> 5, a >> 5);
                if (!chunks.TryGetValue(chunkCoord, out var chunk)) continue;
                int idx = alongX ? ChunkMath.Index(a & 31, y & 31, local) : ChunkMath.Index(local, y & 31, a & 31);
                var (r, g, b) = CellColor(chunk.Data, idx, y, alongX ? a : fixedCoord, alongX ? fixedCoord : a);
                img.Set(a, h - 1 - y, r, g, b);
            }
        img.Save(path);
    }

    public void Slice(string path, Dictionary<Int3, GeneratedChunk> chunks, int y)
    {
        if (chunks.Count == 0) return;
        int minX = chunks.Keys.Min(c => c.X), minZ = chunks.Keys.Min(c => c.Z);
        int w = (chunks.Keys.Max(c => c.X) - minX + 1) * 32, h = (chunks.Keys.Max(c => c.Z) - minZ + 1) * 32;
        var img = new Image(w, h);
        foreach (var (coord, chunk) in chunks)
            for (int z = 0; z < 32; z++)
                for (int x = 0; x < 32; x++)
                {
                    var (r, g, b) = CellColor(chunk.Data, ChunkMath.Index(x, y & 31, z), y, coord.X * 32 + x, coord.Z * 32 + z);
                    img.Set((coord.X - minX) * 32 + x, (coord.Z - minZ) * 32 + z, r, g, b);
                }
        img.Save(path);
    }

    private (float, float, float) CellColor(ChunkData data, int idx, int y, int wx, int wz)
    {
        byte liquid = data.Liquids?[idx] ?? 0;
        ushort block = data.GetBlock(idx);
        if (block == 0)
        {
            if (liquid != 0) return LiquidCode.Type(liquid) switch { LiquidType.Lava => (255, 110, 20), LiquidType.Honey => (230, 170, 40), _ => (40, 90, 200) };
            float surface = _gen.Terrain.SurfaceHeight(wx, wz);
            return y > surface ? (150, 190, 230) : (12, 10, 16);
        }
        var c = _blockColors[block];
        return (c.R, c.G, c.B);
    }

    private static (byte R, byte G, byte B) Parse(string hex) =>
        (byte.Parse(hex.AsSpan(1, 2), NumberStyles.HexNumber), byte.Parse(hex.AsSpan(3, 2), NumberStyles.HexNumber), byte.Parse(hex.AsSpan(5, 2), NumberStyles.HexNumber));

    private sealed class Image
    {
        private readonly byte[] _rgba;

        public Image(int w, int h)
        {
            W = w;
            H = h;
            _rgba = new byte[w * h * 4];
            for (int i = 3; i < _rgba.Length; i += 4) _rgba[i] = 255;
        }

        public int W { get; }
        public int H { get; }

        public void Set(int x, int y, float r, float g, float b)
        {
            if ((uint)x >= W || (uint)y >= H) return;
            int i = (y * W + x) * 4;
            _rgba[i] = (byte)Math.Clamp(r, 0, 255);
            _rgba[i + 1] = (byte)Math.Clamp(g, 0, 255);
            _rgba[i + 2] = (byte)Math.Clamp(b, 0, 255);
        }

        public void Dot(int cx, int cy, int r, float cr, float cg, float cb)
        {
            for (int y = -r; y <= r; y++)
                for (int x = -r; x <= r; x++)
                    if (x * x + y * y <= r * r) Set(cx + x, cy + y, cr, cg, cb);
        }

        public void Rect(int x0, int y0, int x1, int y1, float r, float g, float b)
        {
            for (int x = x0; x <= x1; x++) { Set(x, y0, r, g, b); Set(x, y1, r, g, b); }
            for (int y = y0; y <= y1; y++) { Set(x0, y, r, g, b); Set(x1, y, r, g, b); }
        }

        public void Save(string path) => PngWriter.Write(path, W, H, _rgba);
    }
}
