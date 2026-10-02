using Strata.Core.Common;
using Strata.Core.Content.Defs;
using Strata.Core.Gameplay.Building;
using Strata.Core.Gameplay.Items;
using Strata.Core.Gameplay.Mining;
using Strata.Core.Gameplay.Stats;
using Strata.Core.Session;
using Strata.Core.Voxels;

namespace Strata.Core.Gameplay.Interaction;

public enum UseOutcome
{
    None,
    Cooldown,
    OutOfReach,
    Mined,
    Damaged,
    TooHard,
    HitObject,
    WrongTool,
    PlacedBlock,
    PlacedObject,
    Blocked,
    Reshaped,
    Consumed,
    Swung,
    Shot,
    NoAmmo,
    NoMana,
    PouredLiquid,
    CollectedLiquid,

    /// <summary>Wire laid or cut.</summary>
    Wired,

    /// <summary>A critter was netted.</summary>
    Caught,

    /// <summary>Got into or out of a minecart.</summary>
    Mounted,

    /// <summary>A block was painted or scraped.</summary>
    Painted,

    /// <summary>A grappling hook was thrown or let go.</summary>
    Grappled,
}

/// <summary>A projectile launch requested by an item use (resolved by the projectile system).</summary>
public sealed record ShotRequest(PlayerEntity Player, ItemUseInfo Weapon, ProjectileDef Projectile, ItemDef? Ammo, float Speed, int Damage, float Knockback, int Crit = 0);

/// <summary>
/// Executes the held item's actions for a player: tools against terrain and objects, block and object placement,
/// consumption, melee swings and shooting. Use timing (use time, auto-reuse, mining speed) is enforced here.
/// </summary>
public sealed partial class ItemUseSystem
{
    private readonly GameWorld _world;
    private Determinism.Pcg32 _ammoRng = new(0xA3305);
    private readonly BuffDef? _healingCooldown;
    private readonly BuffDef? _manaCooldown;

    public ItemUseSystem(GameWorld world)
    {
        _world = world;
        var buffs = world.Session.Content.Buffs.All;
        _healingCooldown = buffs.FirstOrDefault(b => b.HasTag("healing_cooldown"));
        _manaCooldown = buffs.FirstOrDefault(b => b.HasTag("mana_cooldown"));
        world.Editor.BlockPlaced += (cell, _) => SprayPlaced(cell);
    }

    /// <summary>A melee swing started (the combat system sweeps its hitbox over the swing).</summary>
    public event Action<PlayerEntity, ItemUseInfo>? Swung;

    public event Action<ShotRequest>? Shot;

    /// <summary>Any use that did something (animation, sound).</summary>
    public event Action<PlayerEntity, ItemUseInfo, UseOutcome>? Used;

    /// <summary>
    /// Uses the held item. <paramref name="freshPress"/> is true on the frame the button went down; holding only
    /// repeats for auto-reuse items, tools and placeables.
    /// </summary>
    public UseOutcome Use(PlayerEntity p, Target? target, bool freshPress)
    {
        var held = p.State.Inventory.Held;
        if (held.IsEmpty || !p.Alive) return UseOutcome.None;
        var info = _world.Uses[held.Item!];
        var item = info.Item;
        _refusal = null;
        // Holding the button keeps tools, placeables and melee weapons going (every sword auto-swings, as in 1.4.4).
        bool repeats = item.AutoReuse || info.IsTool || info.Has(ItemAction.PlaceBlock | ItemAction.PlaceObject | ItemAction.Melee | ItemAction.Thrust);
        if (!freshPress && !repeats) return UseOutcome.None;
        // One animation per use, never restarted mid-arc: a new one starts once the last has played out (or for
        // another item). A tool keeps swinging while its next hit cools down, and hits when its use time is up.
        bool newAnimation = p.UseAnimation <= 0 || p.UseItem != item;
        bool melee = info.Has(ItemAction.Melee | ItemAction.Thrust);
        if (p.UseCooldown > 0 && !(newAnimation && info.IsTool)) return UseOutcome.Cooldown;
        if (!newAnimation && melee && !info.IsTool) return UseOutcome.Cooldown; // one strike per swing
        if (p.State.Effects.NoItemUse) return Report(p, info, Refuse("You can't use items right now"), freshPress);
        float speed = UseSpeed(p, held, info);
        if (p.UseCooldown > 0)
        {
            StartAnimation(p, item, speed);
            if (melee) Swung?.Invoke(p, info);
            Used?.Invoke(p, info, UseOutcome.Swung);
            return UseOutcome.Swung;
        }

        float reach = ReachMeters(p, item);
        var inReach = target != null && target.Distance <= reach ? target : null;
        var outcome = UseOutcome.None;
        bool shotFailed = false;
        float useTime = item.UseTime / speed;

        if (info.Has(ItemAction.PlaceBlock) || info.Has(ItemAction.PlaceObject))
        {
            int placed = 0;
            _placer = p;
            outcome = inReach == null ? UseOutcome.OutOfReach : Place(p, info, inReach, out placed);
            _placer = null;
            if (outcome is UseOutcome.PlacedBlock or UseOutcome.PlacedObject && item.Consumable) ConsumeHeld(p, placed);
        }
        else if (info.IsTool && inReach != null)
        {
            outcome = UseTool(p, info, inReach);
            useTime /= 1f + p.State.Effects.Stats.Bonus(StatIds.MiningSpeed); // more hits per swing, same swing
        }
        else if (info.Has(ItemAction.Bucket))
        {
            outcome = UseBucket(p, info, reach);
        }
        else if (info.Has(ItemAction.SummonBoss))
        {
            outcome = Summon(p, info);
        }
        else if (info.Has(ItemAction.Consume))
        {
            outcome = Consume(p, info);
        }
        else if (info.Has(ItemAction.Fish))
        {
            outcome = Fish(p, info);
        }
        else if (info.Has(ItemAction.Wire))
        {
            outcome = inReach == null ? UseOutcome.OutOfReach : UseWiring(p, info.Wiring!, inReach);
        }
        else if (info.Has(ItemAction.Net))
        {
            outcome = SwingNet(p, info.BugNet!);
        }
        else if (info.Has(ItemAction.Paint))
        {
            outcome = inReach == null ? UseOutcome.OutOfReach : UsePaint(p, info.Painting!, inReach);
        }
        else if (info.Has(ItemAction.Mount))
        {
            outcome = Mount(p, info.Minecart!, inReach);
        }
        else if (info.Has(ItemAction.Release))
        {
            outcome = inReach == null ? UseOutcome.OutOfReach : Release(p, info.Release!, inReach);
        }
        else if (info.Has(ItemAction.Grapple))
        {
            outcome = Grapple(p, info);
        }
        else if (info.Has(ItemAction.Shoot))
        {
            outcome = Shoot(p, info);
            shotFailed = outcome is UseOutcome.NoAmmo or UseOutcome.NoMana or UseOutcome.Blocked;
        }

        // Swings and thrusts (shortswords, spears) both attack through the melee system, once per animation. A blade
        // that also shoots (Starfury, Terra Blade) swings with every shot, and still swings when the shot cannot fire.
        bool swings = melee && newAnimation
            && (shotFailed || outcome is UseOutcome.None or UseOutcome.OutOfReach or UseOutcome.Shot or UseOutcome.Mined or UseOutcome.Damaged or UseOutcome.HitObject or UseOutcome.TooHard or UseOutcome.WrongTool);
        if (swings && (shotFailed || outcome is UseOutcome.None or UseOutcome.OutOfReach)) outcome = UseOutcome.Swung;
        if (outcome is UseOutcome.Blocked or UseOutcome.NoAmmo or UseOutcome.NoMana) return Report(p, info, outcome, freshPress);
        if (outcome is UseOutcome.None or UseOutcome.OutOfReach) return outcome;
        p.UseCooldown = UseTicks(useTime);
        if (newAnimation) StartAnimation(p, item, speed); // mid-animation digs and gun bursts leave the swing alone
        if (swings) Swung?.Invoke(p, info);
        Used?.Invoke(p, info, outcome);
        if (outcome is UseOutcome.Swung or UseOutcome.Shot) LaunchAttackProjectiles(p, info);
        return outcome;
    }

    /// <summary>
    /// How far (m) from the eye the player can use <paramref name="item"/> (10 cells with an empty hand), plus the
    /// Reach stat (Builder Potion and friends). The crosshair's in-reach highlight uses the same value.
    /// </summary>
    public static float ReachMeters(PlayerEntity p, ItemDef? item) =>
        ((item?.Reach ?? 10) + p.State.Effects.Stats.Bonus(StatIds.Reach)) * Units.CellSize;

    /// <summary>
    /// Which soft objects the crosshair stops at for a held item: mining, chopping and hammering tools select decor
    /// (and clear it), only axes select a tree's canopy; weapons, placeables and the empty hand look through both.
    /// </summary>
    public static TargetFilter FilterFor(ItemUseInfo? info)
    {
        if (info == null || !info.IsTool) return TargetFilter.None;
        return info.Has(ItemAction.Chop) ? TargetFilter.Decor | TargetFilter.TreeCanopy : TargetFilter.Decor;
    }

    private UseOutcome UseTool(PlayerEntity p, ItemUseInfo info, Target target)
    {
        var item = info.Item;
        var editor = _world.Editor;
        if (target.Object is { } obj)
        {
            var r = editor.HitObject(obj, item);
            return r switch
            {
                ObjectHitResult.WrongTool => UseOutcome.WrongTool,
                ObjectHitResult.TooWeak => UseOutcome.TooHard,
                ObjectHitResult.NotEmpty => UseOutcome.Blocked,
                _ => UseOutcome.HitObject,
            };
        }
        if (info.Has(ItemAction.Hammer) && !info.Has(ItemAction.Mine))
        {
            int rotation = p.FacingQuarter + p.BuildRotation;
            if (!p.Precision) return editor.ReshapeBlock(target.Cell, p.BuildShape, rotation) ? UseOutcome.Reshaped : UseOutcome.WrongTool;
            return editor.Reshape(target.Cell, ShapeCode.Pack(p.BuildShape, rotation)) ? UseOutcome.Reshaped : UseOutcome.WrongTool;
        }
        if (!info.Has(ItemAction.Mine)) return UseOutcome.WrongTool;
        var normal = target.Normal == Int3.Zero ? DominantFace(p) : target.Normal;
        int size = p.Precision ? 1 : Math.Max(1, item.MineBrush);
        var cells = MiningRules.Brush(target.Cell, normal, target.Fraction, size);
        var result = editor.Mine(cells, item.PickPower, item.PickPower, _world.Session.Clock.Tick);
        if (result.Broken.Count > 0) return UseOutcome.Mined;
        if (result.Damaged.Count > 0) return UseOutcome.Damaged;
        return result.TooHard.Count > 0 ? UseOutcome.TooHard : UseOutcome.None;
    }

    private UseOutcome Place(PlayerEntity p, ItemUseInfo info, Target target, out int placed)
    {
        var editor = _world.Editor;
        placed = 1;
        if (info.Block is { } block)
        {
            var cell = PlacementCell(target);
            var blockers = _world.Players.Where(o => o.Alive).Select(o => o.CellBox);
            if (!p.Precision)
            {
                // A whole grid block (1 m), one item per filled cell.
                var cells = BlockGrid.Pattern(cell, block.Render == BlockRender.Crisp ? p.BuildShape : CellShape.Full, p.FacingQuarter + p.BuildRotation);
                int limit = info.Item.Consumable ? p.State.Inventory.Held.Count : int.MaxValue;
                var (result, count) = editor.PlaceBlocks(cells, block, limit, blockers);
                placed = count;
                return result == PlacementResult.Ok ? UseOutcome.PlacedBlock : UseOutcome.Blocked;
            }
            byte shape = block.Render == BlockRender.Crisp ? ShapeCode.Pack(p.BuildShape, p.FacingQuarter + p.BuildRotation) : (byte)0;
            var r = editor.PlaceBlock(cell, block, shape, blockers);
            return r == PlacementResult.Ok ? UseOutcome.PlacedBlock : UseOutcome.Blocked;
        }
        if (info.Object is { } def)
        {
            int rotation = (p.FacingQuarter + p.BuildRotation) & 3;
            var anchor = ObjectAnchor(def, target, rotation);
            var r = editor.PlaceObject(def, anchor, rotation);
            return r == PlacementResult.Ok ? UseOutcome.PlacedObject : UseOutcome.Blocked;
        }
        return UseOutcome.None;
    }

    /// <summary>The cell a block placed at <paramref name="target"/> goes into: in front of the hit face.</summary>
    public static Int3 PlacementCell(Target target) => target.Normal == Int3.Zero ? target.Cell : target.Adjacent;

    /// <summary>
    /// Minimum corner for an object placed against a target face: centred on the targeted cell, resting on a floor,
    /// hanging from a ceiling or sticking out of a wall.
    /// </summary>
    public static Int3 ObjectAnchor(TileObjectDef def, Target target, int rotation)
    {
        var fp = Voxels.TileObjects.TileObjectInstance.FootprintOf(def, rotation);
        var c = target.Adjacent;
        var n = target.Normal;
        int x = c.X - (n.X != 0 ? (n.X > 0 ? 0 : fp.X - 1) : fp.X / 2);
        int z = c.Z - (n.Z != 0 ? (n.Z > 0 ? 0 : fp.Z - 1) : fp.Z / 2);
        int y = n.Y < 0 ? c.Y - (fp.Y - 1) : c.Y;
        return new Int3(x, y, z);
    }

    private static Int3 DominantFace(PlayerEntity p)
    {
        var a = -p.Aim;
        float ax = MathF.Abs(a.X), ay = MathF.Abs(a.Y), az = MathF.Abs(a.Z);
        if (ay >= ax && ay >= az) return new Int3(0, Math.Sign(a.Y), 0);
        return ax >= az ? new Int3(Math.Sign(a.X), 0, 0) : new Int3(0, 0, Math.Sign(a.Z));
    }
}
