using Strata.Core.Content;
using Strata.Core.Content.Defs;
using Strata.Core.Content.Specs;

namespace Strata.Core.Gameplay.Items;

/// <summary>What using an item does. Several may apply (a pickaxe mines and swings).</summary>
[Flags]
public enum ItemAction
{
    None = 0,
    Mine = 1 << 0,
    Chop = 1 << 1,
    Hammer = 1 << 2,
    Melee = 1 << 3,
    Thrust = 1 << 4,
    Shoot = 1 << 5,
    PlaceBlock = 1 << 6,
    PlaceObject = 1 << 7,
    Consume = 1 << 8,
    SummonBoss = 1 << 9,
    Bucket = 1 << 10,
    Grapple = 1 << 11,
    Fish = 1 << 12,
    Wire = 1 << 13,
    Net = 1 << 14,
    Release = 1 << 15,
    Mount = 1 << 16,
    Paint = 1 << 17,
    Ride = 1 << 18,

    /// <summary>A whip's lash (P3-14).</summary>
    Whip = 1 << 19,
    PlantGrass = 1 << 20,
    License = 1 << 21,
    HairDye = 1 << 22,
}

/// <summary>Resolved use information for one item: inferred actions plus parsed behaviour parameters.</summary>
public sealed class ItemUseInfo
{
    public ItemUseInfo(ItemDef item) => Item = item;

    public ItemDef Item { get; }
    public ItemAction Actions { get; internal set; }

    /// <summary>How the item is used on screen: its data style, or one inferred from its stats (<see cref="UseStyleRules"/>).</summary>
    public UseStyle Style { get; internal set; }
    public MeleeSwingParams? Swing { get; internal set; }
    public ThrustParams? ThrustParams { get; internal set; }
    public ShootProjectileParams? Shoot { get; internal set; }
    public WhipParams? Whip { get; internal set; }
    public ConsumeParams? Consume { get; internal set; }
    public SummonBossParams? Summon { get; internal set; }

    /// <summary>An event the item starts instead of a boss (medallions, globes, crystals).</summary>
    public StartEventParams? StartEvent { get; internal set; }
    public Conditions.ICondition? SummonCondition { get; internal set; }
    public BucketParams? Bucket { get; internal set; }

    /// <summary>Hook parameters (grappling hooks).</summary>
    public GrappleParams? Grapple { get; internal set; }

    /// <summary>Fishing rod parameters.</summary>
    public FishingParams? Fishing { get; internal set; }

    public WiringParams? Wiring { get; internal set; }
    public BugNetParams? BugNet { get; internal set; }
    public ReleaseCritterParams? Release { get; internal set; }
    public MinecartParams? Minecart { get; internal set; }

    /// <summary>A mount summoned by this item (<see cref="ItemAction.Ride"/>).</summary>
    public Mounts.MountParams? Ride { get; internal set; }
    public PaintToolParams? Painting { get; internal set; }
    public PlantGrassParams? Garden { get; internal set; }
    public LicenseParams? License { get; internal set; }

    /// <summary>Hair dye this item applies when used.</summary>
    public HairDyeParams? HairDye { get; internal set; }

    /// <summary>Fishing bait (worms, fireflies, Master Bait).</summary>
    public BaitParams? Bait { get; internal set; }

    /// <summary>Boss this bait hooks (Truffle Worm).</summary>
    public BaitSummonParams? BaitSummon { get; internal set; }

    /// <summary>Boss summoned when this item burns in a liquid (not a use action).</summary>
    public IncinerationSummonParams? Incineration { get; internal set; }

    /// <summary>Liquid a filled bucket pours (null for an empty bucket).</summary>
    public LiquidDef? BucketLiquid { get; internal set; }

    public BlockDef? Block { get; internal set; }
    public TileObjectDef? Object { get; internal set; }

    /// <summary>Projectile fired directly by this item (weapons without ammo, or ammo items).</summary>
    public ProjectileDef? Projectile { get; internal set; }

    public bool Has(ItemAction action) => (Actions & action) != 0;
    public bool IsTool => (Actions & (ItemAction.Mine | ItemAction.Chop | ItemAction.Hammer)) != 0;
}

/// <summary>
/// Resolves every item's use actions once. Actions are inferred from item stats (pick power, place targets) and the
/// use style (a swing hits, a bow shoots, a potion is drunk: <see cref="UseStyleRules"/>) so data does not repeat
/// itself; explicit <c>behaviors</c> add or tune actions.
/// </summary>
public sealed class ItemUseTable
{
    private readonly ContentDatabase _content;
    private ItemUseInfo?[] _infos;

    public ItemUseTable(ContentDatabase content)
    {
        _content = content;
        _infos = new ItemUseInfo?[content.Items.Count];
        foreach (var item in content.Items.All) _infos[item.RuntimeId] = Resolve(item, content);
    }

    /// <summary>Use info for an item; items added after construction (save placeholders) are resolved on demand.</summary>
    public ItemUseInfo this[ItemDef item]
    {
        get
        {
            if (item.RuntimeId >= _infos.Length) Array.Resize(ref _infos, _content.Items.Count);
            return _infos[item.RuntimeId] ??= Resolve(item, _content);
        }
    }

    private static ItemUseInfo Resolve(ItemDef item, ContentDatabase content)
    {
        var info = new ItemUseInfo(item);
        var a = ItemAction.None;
        if (item.PickPower > 0) a |= ItemAction.Mine;
        if (item.AxePower > 0) a |= ItemAction.Chop;
        if (item.HammerPower > 0) a |= ItemAction.Hammer;
        info.Style = UseStyleRules.Resolve(item);
        a |= UseStyleRules.Implied(info.Style, item); // a sword's swing hits, a gun shoots, a potion is drunk
        if (item.PlaceBlock != null && content.Blocks.TryGet(item.PlaceBlock, out var block))
        {
            a |= ItemAction.PlaceBlock;
            info.Block = block;
        }
        if (item.PlaceObject != null && content.TileObjects.TryGet(item.PlaceObject, out var obj))
        {
            a |= ItemAction.PlaceObject;
            info.Object = obj;
        }
        if (item.Projectile != null && content.Projectiles.TryGet(item.Projectile, out var proj)) info.Projectile = proj;

        foreach (var spec in item.Behaviors)
        {
            switch (spec.Type)
            {
                case ItemBehaviorSpecs.MeleeSwing: a |= ItemAction.Melee; info.Swing = spec.Parse<MeleeSwingParams>(); break;
                case ItemBehaviorSpecs.Thrust: a |= ItemAction.Thrust; info.ThrustParams = spec.Parse<ThrustParams>(); break;
                case ItemBehaviorSpecs.ShootProjectile: a |= ItemAction.Shoot; info.Shoot = spec.Parse<ShootProjectileParams>(); break;
                case ItemBehaviorSpecs.Whip: a |= ItemAction.Whip; info.Whip = spec.Parse<WhipParams>(); break;
                case ItemBehaviorSpecs.MineTool: a |= ItemAction.Mine; break;
                case ItemBehaviorSpecs.ChopTool: a |= ItemAction.Chop; break;
                case ItemBehaviorSpecs.HammerTool: a |= ItemAction.Hammer; break;
                case ItemBehaviorSpecs.PlaceBlock: a |= info.Block != null ? ItemAction.PlaceBlock : 0; break;
                case ItemBehaviorSpecs.PlaceObject: a |= info.Object != null ? ItemAction.PlaceObject : 0; break;
                case ItemBehaviorSpecs.Consume: a |= ItemAction.Consume; info.Consume = spec.Parse<ConsumeParams>(); break;
                case ItemBehaviorSpecs.SummonBoss:
                    a |= ItemAction.SummonBoss;
                    info.Summon = spec.Parse<SummonBossParams>();
                    info.SummonCondition = Conditions.ConditionCompiler.CompileAll(info.Summon.Conditions);
                    break;
                case ItemBehaviorSpecs.StartEvent:
                    a |= ItemAction.SummonBoss;
                    info.StartEvent = spec.Parse<StartEventParams>();
                    info.SummonCondition = Conditions.ConditionCompiler.CompileAll(info.StartEvent.Conditions);
                    break;
                case ItemBehaviorSpecs.Bucket:
                    a |= ItemAction.Bucket;
                    info.Bucket = spec.Parse<BucketParams>();
                    info.BucketLiquid = info.Bucket.Liquid != null && content.Liquids.TryGet(info.Bucket.Liquid, out var liquid) ? liquid : null;
                    break;
                case ItemBehaviorSpecs.IncinerationSummon: info.Incineration = spec.Parse<IncinerationSummonParams>(); break;
                case ItemBehaviorSpecs.Grapple: a |= ItemAction.Grapple; info.Grapple = spec.Parse<GrappleParams>(); break;
                case ItemBehaviorSpecs.Fishing: a |= ItemAction.Fish; info.Fishing = spec.Parse<FishingParams>(); break;
                case ItemBehaviorSpecs.BaitSummon: info.BaitSummon = spec.Parse<BaitSummonParams>(); break;
                case ItemBehaviorSpecs.Bait: info.Bait = spec.Parse<BaitParams>(); break;
                case ItemBehaviorSpecs.Wiring: a |= ItemAction.Wire; info.Wiring = spec.Parse<WiringParams>(); break;
                case ItemBehaviorSpecs.BugNet: a |= ItemAction.Net; info.BugNet = spec.Parse<BugNetParams>(); break;
                case ItemBehaviorSpecs.ReleaseCritter: a |= ItemAction.Release; info.Release = spec.Parse<ReleaseCritterParams>(); break;
                case ItemBehaviorSpecs.Minecart: a |= ItemAction.Mount; info.Minecart = spec.Parse<MinecartParams>(); break;
                case ItemBehaviorSpecs.Mount: a |= ItemAction.Ride; info.Ride = spec.Parse<Mounts.MountParams>(); break;
                case ItemBehaviorSpecs.PaintTool: a |= ItemAction.Paint; info.Painting = spec.Parse<PaintToolParams>(); break;
                case ItemBehaviorSpecs.PlantGrass: a |= ItemAction.PlantGrass; info.Garden = spec.Parse<PlantGrassParams>(); break;
                case ItemBehaviorSpecs.License: a |= ItemAction.License; info.License = spec.Parse<LicenseParams>(); break;
                case ItemBehaviorSpecs.HairDye: a |= ItemAction.HairDye; info.HairDye = spec.Parse<HairDyeParams>(); break;
                default: throw new InvalidOperationException($"Item '{item.Id}' has unknown behaviour '{spec.Type}' (validator should have rejected it).");
            }
        }
        if ((a & ItemAction.Melee) != 0) info.Swing ??= new MeleeSwingParams { Type = ItemBehaviorSpecs.MeleeSwing };
        if ((a & ItemAction.Thrust) != 0) info.ThrustParams ??= new ThrustParams { Type = ItemBehaviorSpecs.Thrust };
        if ((a & ItemAction.Shoot) != 0) info.Shoot ??= new ShootProjectileParams { Type = ItemBehaviorSpecs.ShootProjectile };
        if ((a & ItemAction.Whip) != 0) info.Whip ??= new WhipParams { Type = ItemBehaviorSpecs.Whip };
        if ((a & ItemAction.Consume) != 0) info.Consume ??= new ConsumeParams { Type = ItemBehaviorSpecs.Consume };
        info.Actions = a;
        return info;
    }
}
