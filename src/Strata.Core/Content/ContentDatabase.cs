using Strata.Core.Content.Defs;

namespace Strata.Core.Content;

/// <summary>All loaded game content, organized in registries. Immutable after loading.</summary>
public sealed class ContentDatabase
{
    public const string AirId = "air";

    public Registry<BlockDef> Blocks { get; } = new("block");
    public Registry<ItemDef> Items { get; } = new("item");
    public Registry<TileObjectDef> TileObjects { get; } = new("tileObject");
    public Registry<RecipeDef> Recipes { get; } = new("recipe");
    public Registry<RecipeGroupDef> RecipeGroups { get; } = new("recipeGroup");
    public Registry<BiomeDef> Biomes { get; } = new("biome");
    public Registry<EnemyDef> Enemies { get; } = new("enemy");
    public Registry<SpawnRuleDef> SpawnRules { get; } = new("spawnRule");
    public Registry<ProjectileDef> Projectiles { get; } = new("projectile");
    public Registry<BuffDef> Buffs { get; } = new("buff");
    public Registry<PrefixDef> Prefixes { get; } = new("prefix");
    public Registry<LootTableDef> LootTables { get; } = new("lootTable");
    public Registry<ProgressionFlagDef> Flags { get; } = new("flag");
    public Registry<OreVeinDef> OreVeins { get; } = new("oreVein");
    public Registry<EventDef> Events { get; } = new("event");
    public Registry<LiquidDef> Liquids { get; } = new("liquid");
    public Registry<TownNpcDef> TownNpcs { get; } = new("townNpc");
    public Registry<WorldMutationDef> Mutations { get; } = new("mutation");
    public Registry<ConversionDef> Conversions { get; } = new("conversion");
    public Registry<SpreadRuleDef> SpreadRules { get; } = new("spreadRule");

    /// <summary>Problems found while loading/validating. Check <see cref="ContentReport.HasErrors"/>.</summary>
    public ContentReport Report { get; } = new();

    /// <summary>Block runtime id of air (always 0).</summary>
    public const ushort Air = 0;

    /// <summary>Resolved drop item per block runtime id (null = drops nothing).</summary>
    public ItemDef?[] BlockDrops { get; private set; } = Array.Empty<ItemDef?>();

    /// <summary>Item that places each block runtime id (null = not placeable).</summary>
    public ItemDef?[] BlockPlacers { get; private set; } = Array.Empty<ItemDef?>();

    /// <summary>Liquid per voxel liquid code 0–7 (index 0 and unused codes are null).</summary>
    public LiquidDef?[] LiquidByCode { get; private set; } = new LiquidDef?[LiquidDef.MaxCode + 1];

    /// <summary>Block runtime id formed where liquid codes a and b meet (<c>[a * 8 + b]</c>, 0 = they do not react).</summary>
    public ushort[] LiquidMixes { get; private set; } = new ushort[(LiquidDef.MaxCode + 1) * (LiquidDef.MaxCode + 1)];

    public IEnumerable<IRegistry> AllRegistries => new IRegistry[]
    {
        Blocks, Items, TileObjects, Recipes, RecipeGroups, Biomes, Enemies, SpawnRules, Projectiles, Buffs,
        Prefixes, LootTables, Flags, OreVeins, Events, Liquids, TownNpcs, Mutations, Conversions, SpreadRules,
    };

    internal ContentDatabase()
    {
        Blocks.TryAdd(new BlockDef { Id = AirId, Name = "Air", Render = BlockRender.None, Solid = false, Sound = "none" });
    }

    /// <summary>Creates an empty database with only built-ins (for tests building content in code).</summary>
    public static ContentDatabase CreateEmpty() => new();

    internal void Finish()
    {
        foreach (var reg in AllRegistries) ((IFreezable)reg).Freeze();

        if (Blocks.Count > ushort.MaxValue) Report.Error("content", "blocks", "More than 65535 blocks.");

        BlockDrops = new ItemDef?[Blocks.Count];
        BlockPlacers = new ItemDef?[Blocks.Count];
        foreach (var block in Blocks.All)
        {
            BlockDrops[block.RuntimeId] = block.Drop != null ? Items.Find(block.Drop) : Items.Find(block.Id);
        }
        foreach (var item in Items.All)
        {
            if (item.PlaceBlock != null && Blocks.TryGet(item.PlaceBlock, out var b)) BlockPlacers[b.RuntimeId] ??= item;
        }
        ResolveLiquids();
    }

    private void ResolveLiquids()
    {
        const int n = LiquidDef.MaxCode + 1;
        foreach (var liquid in Liquids.All)
            if (liquid.Code is >= 1 and <= LiquidDef.MaxCode) LiquidByCode[liquid.Code] ??= liquid;
        foreach (var liquid in Liquids.All)
        {
            if (LiquidByCode[liquid.Code] != liquid) continue;
            foreach (var mix in liquid.Mixes)
            {
                if (!Liquids.TryGet(mix.With, out var other) || !Blocks.TryGet(mix.Block, out var block)) continue;
                int a = liquid.Code, b = other.Code;
                if (LiquidMixes[a * n + b] == 0) LiquidMixes[a * n + b] = (ushort)block.RuntimeId;
                if (LiquidMixes[b * n + a] == 0) LiquidMixes[b * n + a] = (ushort)block.RuntimeId;
            }
        }
    }

    /// <summary>Liquid definition for a voxel liquid byte (null when empty or unknown).</summary>
    public LiquidDef? LiquidOf(byte code) => code == 0 ? null : LiquidByCode[code >> 5];

    /// <summary>Block formed where liquid codes a and b meet (0 = none).</summary>
    public ushort MixOf(int a, int b) => LiquidMixes[a * (LiquidDef.MaxCode + 1) + b];

    public BlockDef Block(ushort runtimeId) => Blocks[runtimeId];

    /// <summary>
    /// Returns a stand-in block for an id found in a save but missing from content, preserving the
    /// original id string so re-saving never loses data.
    /// </summary>
    /// <summary>Old content ids and their new names (data/config/aliases.json): saves from before a rename resolve through it.</summary>
    public Dictionary<string, string> Aliases { get; } = new(StringComparer.Ordinal);

    /// <summary>The current id for an id read from a save (renamed ids follow their alias).</summary>
    public string Current(string id) => Aliases.TryGetValue(id, out var renamed) ? renamed : id;

    public BlockDef GetOrAddPlaceholderBlock(string id)
    {
        id = Current(id);
        if (Blocks.TryGet(id, out var existing)) return existing;
        lock (Blocks)
        {
            var def = Blocks.AddLate(new BlockDef
            {
                Id = id, Name = $"Missing block ({id})", Render = BlockRender.Crisp,
                Textures = new BlockTextures { All = "missing" }, Tags = new[] { "placeholder" }, AutoItem = false,
            });
            if (BlockDrops.Length < Blocks.Count)
            {
                var drops = BlockDrops;
                var placers = BlockPlacers;
                Array.Resize(ref drops, Blocks.Count);
                Array.Resize(ref placers, Blocks.Count);
                BlockDrops = drops;
                BlockPlacers = placers;
            }
            return def;
        }
    }

    public ItemDef GetOrAddPlaceholderItem(string id) =>
        Items.TryGet(id = Current(id), out var existing)
            ? existing
            : Items.AddLate(new ItemDef { Id = id, Name = $"Missing item ({id})", MaxStack = ItemDef.CommonMaxStack, Tags = new[] { "placeholder" } });

    public TileObjectDef GetOrAddPlaceholderTileObject(string id) =>
        TileObjects.TryGet(id = Current(id), out var existing)
            ? existing
            : TileObjects.AddLate(new TileObjectDef { Id = id, Name = $"Missing object ({id})", Natural = true, AutoItem = false, Tags = new[] { "placeholder" } });

    public PrefixDef GetOrAddPlaceholderPrefix(string id) =>
        Prefixes.TryGet(id = Current(id), out var existing)
            ? existing
            : Prefixes.AddLate(new PrefixDef { Id = id, Name = id, Applies = new[] { "universal" }, Tags = new[] { "placeholder" } });

    public ushort BlockId(string id) => (ushort)Blocks.Get(id).RuntimeId;
}
