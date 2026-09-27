using System.Collections.Concurrent;
using Strata.Core.Content;

namespace Strata.Core.WorldGen.Fields;

/// <summary>Resolved runtime ids of blocks used by world generation (resolved once, read concurrently).</summary>
public sealed class BlockPalette
{
    private readonly ContentDatabase _content;
    private readonly ConcurrentDictionary<string, ushort> _cache = new(StringComparer.Ordinal);

    public BlockPalette(ContentDatabase content)
    {
        _content = content;
        Air = ContentDatabase.Air;
        Bedrock = Id("abyssal_rock");
        Stone = Id("stone");
        Deepstone = Id("deepstone");
        Loam = Id("dirt");
        Turf = Id("grass");
        Clay = Id("clay");
        Sand = Id("sand");
        Sandstone = Id("sandstone");
        PackedSand = Id("hardened_sand");
        Silt = Id("silt");
        Mud = Id("mud");
        JungleTurf = Id("jungle_grass");
        Snow = Id("snow");
        Ice = Id("ice");
        Slush = Id("slush");
        Ash = Id("ash");
        Obsidian = Id("obsidian");
        Marble = Id("marble");
        Granite = Id("granite");
        UmbralStone = Id("ebonstone");
        UmbralTurf = Id("corrupt_grass");
        SanguineStone = Id("crimstone");
        SanguineTurf = Id("crimson_grass");
        MushroomTurf = Id("mushroom_grass");
        Cloud = Id("cloud");
        RainCloud = Id("rain_cloud");
        Hive = Id("hive");
        Aetherium = Id("aetherium");
        MossStone = Id("green_moss");
        GlowMossStone = Id("argon_moss");
        Crystal = Id("crystal_cluster");
        Cobweb = Id("cobweb");
        Cinderstone = Id("hellstone");
    }

    public ushort Air { get; }
    public ushort Bedrock { get; }
    public ushort Stone { get; }
    public ushort Deepstone { get; }
    public ushort Loam { get; }
    public ushort Turf { get; }
    public ushort Clay { get; }
    public ushort Sand { get; }
    public ushort Sandstone { get; }
    public ushort PackedSand { get; }
    public ushort Silt { get; }
    public ushort Mud { get; }
    public ushort JungleTurf { get; }
    public ushort Snow { get; }
    public ushort Ice { get; }
    public ushort Slush { get; }
    public ushort Ash { get; }
    public ushort Obsidian { get; }
    public ushort Marble { get; }
    public ushort Granite { get; }
    public ushort UmbralStone { get; }
    public ushort UmbralTurf { get; }
    public ushort SanguineStone { get; }
    public ushort SanguineTurf { get; }
    public ushort MushroomTurf { get; }
    public ushort Cloud { get; }
    public ushort RainCloud { get; }
    public ushort Hive { get; }
    public ushort Aetherium { get; }
    public ushort MossStone { get; }
    public ushort GlowMossStone { get; }
    public ushort Crystal { get; }
    public ushort Cobweb { get; }
    public ushort Cinderstone { get; }

    public ushort Id(string blockId) => _cache.GetOrAdd(blockId, id => (ushort)_content.Blocks.Get(id).RuntimeId);

    private bool[]? _natural;

    public bool IsNatural(ushort block)
    {
        var table = _natural ??= _content.Blocks.All.Select(b => b.HasTag("natural")).ToArray();
        return block != Air && block < table.Length && table[block];
    }
}
