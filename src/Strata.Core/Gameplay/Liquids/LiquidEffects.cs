using System.Numerics;
using Strata.Core.Common;
using Strata.Core.Content;
using Strata.Core.Content.Defs;
using Strata.Core.Determinism;
using Strata.Core.Gameplay.Combat;
using Strata.Core.Gameplay.Entities;
using Strata.Core.Gameplay.Items;
using Strata.Core.Session;
using Strata.Core.Voxels;

namespace Strata.Core.Gameplay.Liquids;

/// <summary>
/// What touching liquid does, all from <see cref="LiquidDef"/> data: contact damage and buffs for players and
/// creatures (lava burns, honey sweetens), buffs cleared by the liquid (water puts out fire), drag and buoyancy on
/// dropped items, and items destroyed by liquids that incinerate them. Also records which liquid each player is in
/// for movement and the HUD.
/// </summary>
public sealed partial class LiquidEffects
{
    /// <summary>Ticks between contact checks for creatures (spread over creatures by id).</summary>
    public const int CreatureInterval = 30;

    /// <summary>Items with this tag survive liquids that destroy items.</summary>
    public const string LiquidProofTag = "liquid_proof";

    private readonly GameWorld _world;
    private readonly ContentDatabase _content;

    public LiquidEffects(GameWorld world)
    {
        _world = world;
        _content = world.Session.Content;
    }

    /// <summary>A dropped item fell into a liquid that destroys it.</summary>
    public event Action<DroppedItem, LiquidDef>? ItemDestroyed;

    /// <summary>
    /// The liquid touching a body whose feet are at <paramref name="feet"/> (m): the lowest liquid in its column
    /// whose surface rises above the feet, and whether the surface is above <paramref name="height"/> × 0.9.
    /// </summary>
    public (LiquidDef? Liquid, bool Submerged) Probe(Vector3 feet, float height)
    {
        var world = _world.Session.World;
        int x = (int)MathF.Floor(feet.X / Units.CellSize), z = (int)MathF.Floor(feet.Z / Units.CellSize);
        int y0 = (int)MathF.Floor(feet.Y / Units.CellSize), y1 = (int)MathF.Floor((feet.Y + height) / Units.CellSize);
        float top = feet.Y + height * 0.9f;
        LiquidDef? touching = null;
        for (int y = y0; y <= y1; y++)
        {
            byte l = world.GetLiquid(x, y, z);
            if (l == 0) continue;
            float surface = (y + (l & 0x1F) / (float)LiquidCode.MaxLevel) * Units.CellSize;
            if (surface <= feet.Y + 0.05f) continue;
            touching ??= _content.LiquidOf(l);
            if (surface >= top) return (touching, true);
        }
        return (touching, false);
    }

    /// <summary>The liquid in the cell containing a point (m).</summary>
    public LiquidDef? At(Vector3 point)
    {
        var c = new Int3((int)MathF.Floor(point.X / Units.CellSize), (int)MathF.Floor(point.Y / Units.CellSize), (int)MathF.Floor(point.Z / Units.CellSize));
        return _content.LiquidOf(_world.Session.World.GetLiquid(c.X, c.Y, c.Z));
    }

    public void Tick()
    {
        long tick = _world.Session.Clock.Tick;
        foreach (var p in _world.Players)
        {
            if (!p.Alive)
            {
                p.Liquid = null;
                p.Submerged = false;
                continue;
            }
            (p.Liquid, p.Submerged) = Probe(p.Position, PlayerEntity.HalfExtents.Y * 2f);
            Breathe(p, tick);
            if (p.Liquid != null) TouchPlayer(p, p.Liquid, tick);
        }
        foreach (var npc in _world.Npcs.All.ToList())
        {
            if (!npc.Alive || (tick + npc.Id) % CreatureInterval != 0) continue;
            var (liquid, _) = Probe(npc.Position, npc.HalfExtents.Y * 2f);
            if (liquid != null) TouchCreature(npc, liquid);
        }
        TickDrops();
    }

    /// <summary>Damage per drowning pulse once breath runs out (every half second).</summary>
    public const int DrownDamage = 10;

    /// <summary>Breath runs down under a drowning liquid (water-breathing effects prevent it) and refills in air.</summary>
    private void Breathe(PlayerEntity p, long tick)
    {
        bool holding = p.Submerged && p.Liquid is { Drowns: true } && !p.State.Effects.WaterBreathing;
        if (!holding)
        {
            p.Breath = Math.Min(PlayerEntity.MaxBreath, p.Breath + 6);
            return;
        }
        if (p.Breath > 0)
        {
            p.Breath--;
            return;
        }
        if (tick % 30 != 0) return;
        p.State.Vitals.DamageDirect(DrownDamage);
        _world.Npcs.ReportPlayerDamage(p, DrownDamage, Vector3.Zero);
    }

    private void TouchPlayer(PlayerEntity p, LiquidDef liquid, long tick)
    {
        var state = p.State;
        bool changed = false;
        if (liquid.ClearsBuffTags.Length > 0)
            foreach (var active in state.Buffs.Active.ToList())
                if (liquid.ClearsBuffTags.Any(active.Def.HasTag))
                {
                    state.Buffs.Remove(active.Def.Id);
                    changed = true;
                }
        if (liquid.ContactBuff != null && _content.Buffs.TryGet(liquid.ContactBuff, out var buff))
        {
            // Refresh only when it has run down a little, so the buff list is not rebuilt every tick.
            var current = state.Buffs.Active.FirstOrDefault(b => b.Def == buff);
            if (current == null || current.TicksLeft < liquid.ContactBuffTicks - 30)
            {
                state.Buffs.Add(buff, liquid.ContactBuffTicks, state.Effects.Immunities);
                changed = true;
            }
        }
        if (changed) state.Recompute();
        if (liquid.ContactDamage <= 0 || state.Effects.LiquidImmunities.Contains(liquid.Id) || state.Vitals.ImmunityTicks > 0) return;
        var rng = new Pcg32(Hash.Combine(Hash.String(liquid.Id), tick));
        var hit = DamageModel.Roll(liquid.ContactDamage, DamageClass.None, 0, 0f, null, state.Defense,
            DamageModel.DefenseFactor(_world.Session.Settings.Difficulty), state.Effects.Stats.Bonus(Stats.StatIds.EnduranceReduction), 1f, ref rng);
        if (state.Vitals.Hurt(hit.Damage, NpcSystem.PlayerHitImmunity))
            _world.Npcs.ReportPlayerDamage(p, hit.Damage, new Vector3(0, 7f, 0)); // lava throws you upward
    }

    private void TouchCreature(Npc npc, LiquidDef liquid)
    {
        if (npc.Def.HasTag(liquid.Id + "_immune")) return;
        if (liquid.ContactBuff != null && _content.Buffs.TryGet(liquid.ContactBuff, out var buff) && buff.Debuff)
            _world.Npcs.AddBuff(npc, buff, liquid.ContactBuffTicks);
        if (liquid.ContactDamage > 0) _world.Npcs.Hit(npc, null, liquid.ContactDamage, DamageClass.None, 0, 0f, npc.Center, melee: false);
    }

    private void TickDrops()
    {
        const float dt = 1f / Units.TicksPerSecond;
        var drops = _world.Drops;
        for (int i = drops.Items.Count - 1; i >= 0; i--)
        {
            var it = drops.Items[i];
            var liquid = At(it.Position);
            if (liquid == null) continue;
            if (liquid.DestroysItems && !it.Stack.Item!.HasTag(LiquidProofTag))
            {
                drops.Remove(it);
                ItemDestroyed?.Invoke(it, liquid);
                continue;
            }
            if (liquid.TransmutesItems)
            {
                ShimmerDrop(it);
                continue;
            }
            // Drag, and buoyancy that nearly cancels gravity so items sink slowly.
            it.Velocity *= liquid.Drag;
            it.Velocity.Y += DroppedItemSystem.Gravity * 0.85f * dt;
        }
    }
}
