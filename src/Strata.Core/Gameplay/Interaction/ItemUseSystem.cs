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
}

/// <summary>A projectile launch requested by an item use (resolved by the projectile system).</summary>
public sealed record ShotRequest(PlayerEntity Player, ItemUseInfo Weapon, ProjectileDef Projectile, ItemDef? Ammo, float Speed, int Damage, float Knockback);

/// <summary>
/// Executes the held item's actions for a player: tools against terrain and objects, block and object placement,
/// consumption, melee swings and shooting. Use timing (use time, auto-reuse, mining speed) is enforced here.
/// </summary>
public sealed class ItemUseSystem
{
    private readonly GameWorld _world;
    private readonly BuffDef? _healingCooldown;
    private readonly BuffDef? _manaCooldown;

    public ItemUseSystem(GameWorld world)
    {
        _world = world;
        var buffs = world.Session.Content.Buffs.All;
        _healingCooldown = buffs.FirstOrDefault(b => b.HasTag("healing_cooldown"));
        _manaCooldown = buffs.FirstOrDefault(b => b.HasTag("mana_cooldown"));
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
        bool repeats = item.AutoReuse || info.IsTool || info.Has(ItemAction.PlaceBlock) || info.Has(ItemAction.PlaceObject);
        if (!freshPress && !repeats) return UseOutcome.None;
        if (p.UseCooldown > 0) return UseOutcome.Cooldown;

        float reach = (item.Reach + p.State.Effects.Stats.Bonus(StatIds.Reach)) * Units.CellSize;
        var inReach = target != null && target.Distance <= reach ? target : null;
        var outcome = UseOutcome.None;
        int useTime = item.UseTime;

        if (info.Has(ItemAction.PlaceBlock) || info.Has(ItemAction.PlaceObject))
        {
            outcome = inReach == null ? UseOutcome.OutOfReach : Place(p, info, inReach);
            if (outcome is UseOutcome.PlacedBlock or UseOutcome.PlacedObject && item.Consumable) ConsumeHeld(p);
        }
        else if (info.IsTool && inReach != null)
        {
            outcome = UseTool(p, info, inReach);
            useTime = (int)MathF.Ceiling(useTime / (1f + p.State.Effects.Stats.Bonus(StatIds.MiningSpeed)));
        }
        else if (info.Has(ItemAction.SummonBoss))
        {
            outcome = Summon(p, info);
        }
        else if (info.Has(ItemAction.Consume))
        {
            outcome = Consume(p, info);
        }
        else if (info.Has(ItemAction.Shoot))
        {
            outcome = Shoot(p, info);
        }

        if (info.Has(ItemAction.Melee) && outcome is UseOutcome.None or UseOutcome.OutOfReach or UseOutcome.Mined or UseOutcome.Damaged or UseOutcome.HitObject or UseOutcome.TooHard or UseOutcome.WrongTool)
        {
            Swung?.Invoke(p, info);
            if (outcome is UseOutcome.None or UseOutcome.OutOfReach) outcome = UseOutcome.Swung;
            useTime = (int)MathF.Ceiling(useTime / (1f + p.State.Effects.Stats.Bonus(StatIds.MeleeSpeed)));
        }
        if (outcome is UseOutcome.None or UseOutcome.OutOfReach or UseOutcome.Blocked or UseOutcome.NoAmmo or UseOutcome.NoMana) return outcome;
        p.UseCooldown = Math.Max(1, useTime);
        p.UseAnimation = Math.Max(1, item.UseAnimation);
        Used?.Invoke(p, info, outcome);
        return outcome;
    }

    private UseOutcome UseTool(PlayerEntity p, ItemUseInfo info, Target target)
    {
        var item = info.Item;
        var editor = _world.Editor;
        if (target.Object is { } obj)
        {
            var r = editor.HitObject(obj, item);
            return r == ObjectHitResult.WrongTool ? UseOutcome.WrongTool : r == ObjectHitResult.NotEmpty ? UseOutcome.Blocked : UseOutcome.HitObject;
        }
        if (info.Has(ItemAction.Hammer) && !info.Has(ItemAction.Mine))
        {
            byte shape = ShapeCode.Pack(p.BuildShape, p.FacingQuarter + p.BuildRotation);
            return editor.Reshape(target.Cell, shape) ? UseOutcome.Reshaped : UseOutcome.WrongTool;
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

    private UseOutcome Place(PlayerEntity p, ItemUseInfo info, Target target)
    {
        var editor = _world.Editor;
        if (info.Block is { } block)
        {
            var cell = target.Normal == Int3.Zero ? target.Cell : target.Adjacent;
            byte shape = block.Render == BlockRender.Crisp ? ShapeCode.Pack(p.BuildShape, p.FacingQuarter + p.BuildRotation) : (byte)0;
            var blockers = _world.Players.Where(o => o.Alive).Select(o => o.CellBox);
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

    private UseOutcome Consume(PlayerEntity p, ItemUseInfo info)
    {
        var item = info.Item;
        var state = p.State;
        var cfg = info.Consume!;
        if (item.HealLife > 0 && _healingCooldown != null && state.Buffs.Has(_healingCooldown.Id)) return UseOutcome.Blocked;
        if (cfg.PermanentLife > 0 && Player.PlayerState.BaseLife + state.BonusLife >= cfg.LifeCap) return UseOutcome.Blocked;
        if (cfg.PermanentMana > 0 && Player.PlayerState.BaseMana + state.BonusMana >= cfg.ManaCap) return UseOutcome.Blocked;

        if (cfg.PermanentLife > 0) state.BonusLife += cfg.PermanentLife;
        if (cfg.PermanentMana > 0) state.BonusMana += cfg.PermanentMana;
        if (item.HealLife > 0)
        {
            state.Vitals.Heal(item.HealLife);
            if (_healingCooldown != null) state.Buffs.Add(_healingCooldown, _healingCooldown.Duration);
        }
        if (item.HealMana > 0)
        {
            state.Vitals.RestoreMana(item.HealMana);
            if (_manaCooldown != null) state.Buffs.Add(_manaCooldown, _manaCooldown.Duration);
        }
        if (item.Buff != null && _world.Session.Content.Buffs.TryGet(item.Buff.Id, out var buff))
            state.Buffs.Add(buff, item.Buff.Ticks, state.Effects.Immunities);
        state.Recompute();
        if (cfg.PermanentLife > 0) state.Vitals.Heal(cfg.PermanentLife);
        if (cfg.PermanentMana > 0) state.Vitals.RestoreMana(cfg.PermanentMana);
        if (item.Consumable) ConsumeHeld(p);
        return UseOutcome.Consumed;
    }

    /// <summary>Raised when a summon fails its conditions (message for the player).</summary>
    public event Action<PlayerEntity, string>? Refused;

    private UseOutcome Summon(PlayerEntity p, ItemUseInfo info)
    {
        var cfg = info.Summon!;
        var content = _world.Session.Content;
        if (!content.Enemies.TryGet(cfg.Enemy, out var boss)) return UseOutcome.Blocked;
        if (_world.Npcs.All.Any(n => n.Def == boss && n.Alive)) return UseOutcome.Blocked;
        var ctx = _world.Session.ContextAt(p.FeetCell);
        if (!info.SummonCondition!.Evaluate(ctx))
        {
            if (cfg.FailMessage != null) Refused?.Invoke(p, cfg.FailMessage);
            return UseOutcome.Blocked;
        }
        var flat = System.Numerics.Vector3.Normalize(new System.Numerics.Vector3(p.Aim.X, 0, p.Aim.Z) + new System.Numerics.Vector3(1e-4f, 0, 0));
        var at = p.Position + flat * cfg.Distance;
        if (!boss.Gravity) at.Y += 8f; // fliers arrive from above
        else if (boss.Boss is { Segments: > 0 }) at.Y -= 10f; // burrowers rise from below
        else at.Y += 6f; // hoppers drop in
        if (_world.Npcs.Spawn(boss, at) == null) return UseOutcome.Blocked;
        if (info.Item.Consumable) ConsumeHeld(p);
        return UseOutcome.Consumed;
    }

    private UseOutcome Shoot(PlayerEntity p, ItemUseInfo info)
    {
        var item = info.Item;
        var state = p.State;
        var projectile = info.Projectile;
        ItemDef? ammo = null;
        if (item.UseAmmo != null)
        {
            int slot = FindAmmo(state.Inventory.Backpack, item.UseAmmo);
            if (slot < 0) return UseOutcome.NoAmmo;
            ammo = state.Inventory.Backpack[slot].Item!;
            projectile ??= _world.Uses[ammo].Projectile;
            if (projectile == null) return UseOutcome.NoAmmo;
            if (ammo.Consumable) state.Inventory.Backpack.Take(slot, 1);
        }
        if (projectile == null) return UseOutcome.None;
        if (item.ManaCost > 0)
        {
            int cost = (int)MathF.Round(item.ManaCost * state.Effects.Stats.Get(StatIds.ManaCost, 1f));
            if (!state.Vitals.TrySpendMana(cost)) return UseOutcome.NoMana;
        }
        int damage = item.Damage + (ammo?.Damage ?? 0);
        float knockback = item.Knockback + (ammo?.Knockback ?? 0f);
        var shot = info.Shoot!;
        for (int i = 0; i < shot.Count; i++)
            Shot?.Invoke(new ShotRequest(p, info, projectile, ammo, item.ShootSpeed, damage, knockback));
        return UseOutcome.Shot;
    }

    /// <summary>Backpack slot of the first stack of the given ammo class, or −1.</summary>
    public static int FindAmmo(Inventory backpack, string ammoClass)
    {
        for (int i = 0; i < backpack.Size; i++)
        {
            var s = backpack[i];
            if (!s.IsEmpty && s.Item!.AmmoClass == ammoClass) return i;
        }
        return -1;
    }

    private static void ConsumeHeld(PlayerEntity p)
    {
        var inv = p.State.Inventory;
        inv.Backpack.Take(inv.SelectedHotbar, 1);
    }

    private static Int3 DominantFace(PlayerEntity p)
    {
        var a = -p.Aim;
        float ax = MathF.Abs(a.X), ay = MathF.Abs(a.Y), az = MathF.Abs(a.Z);
        if (ay >= ax && ay >= az) return new Int3(0, Math.Sign(a.Y), 0);
        return ax >= az ? new Int3(Math.Sign(a.X), 0, 0) : new Int3(0, 0, Math.Sign(a.Z));
    }
}
