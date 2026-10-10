using System.Numerics;
using Strata.Core.Common;
using Strata.Core.Content.Specs;
using Strata.Core.Gameplay.Items;
using Strata.Core.Session;

namespace Strata.Core.Gameplay.Interaction;

public sealed partial class ItemUseSystem
{
    /// <summary>
    /// A wrench lays wire of its colour through the aimed cell (using one Wire); an Actuator goes onto the aimed block
    /// (using itself); the Wire Cutter takes every wire and the actuator back out of the cell, returning them.
    /// </summary>
    private UseOutcome UseWiring(PlayerEntity p, WiringParams cfg, Target target)
    {
        var cell = target.Cell;
        var wires = _world.Wires;
        var items = _world.Session.Content.Items;
        if (cfg.Cut)
        {
            int wireCount = 0;
            foreach (var c in Wiring.WireColors.All)
                if (wires.Has(cell, c)) wireCount++;
            bool actuator = wires.Grid.HasActuator(cell);
            if (wires.Cut(cell) == 0) return UseOutcome.Blocked;
            if (wireCount > 0 && items.TryGet(cfg.Wire, out var wireItem)) Give(p, new ItemStack(wireItem, wireCount));
            if (actuator && items.TryGet("actuator", out var act)) Give(p, new ItemStack(act, 1));
            return UseOutcome.Wired;
        }
        var color = Wiring.WireColors.Parse(cfg.Color);
        if (cfg.Actuator ? wires.Grid.HasActuator(cell) : wires.Has(cell, color)) return UseOutcome.Blocked;
        var backpack = p.State.Inventory.Backpack;
        for (int i = 0; i < backpack.Size; i++)
        {
            if (backpack[i].Item?.Id != cfg.Wire) continue;
            if (cfg.Actuator ? !wires.PlaceActuator(cell) : !wires.Place(cell, color)) return UseOutcome.Blocked;
            backpack.Take(i, 1);
            return UseOutcome.Wired;
        }
        return UseOutcome.NoAmmo;
    }

    /// <summary>Bug Net: catches the nearest critter in front of the player within reach.</summary>
    private UseOutcome SwingNet(PlayerEntity p, BugNetParams cfg)
    {
        float reach = cfg.Range * Units.CellSize;
        Entities.Npc? best = null;
        float bestDistance = float.MaxValue;
        foreach (var npc in _world.Npcs.All)
        {
            if (!npc.Alive || npc.FromStatue || !npc.Def.HasTag(cfg.Tag) || npc.Def.CatchItem == null) continue; // statue critters are not for keeping
            var to = npc.Center - p.Eye;
            float d = to.Length();
            if (d > reach + npc.HalfExtents.X || d >= bestDistance) continue;
            if (d > 0.5f && Vector3.Dot(to / d, p.Aim) < 0.5f) continue; // behind or beside the swing
            best = npc;
            bestDistance = d;
        }
        if (best == null || !_world.Session.Content.Items.TryGet(best.Def.CatchItem!, out var item)) return UseOutcome.Swung;
        NetCaught?.Invoke(p, best);
        _world.Npcs.Despawn(best);
        Give(p, new ItemStack(item, 1));
        return UseOutcome.Caught;
    }

    /// <summary>The Minecart Upgrade Kit's Mechanical Cart: 25% faster top speed and acceleration.</summary>
    public const float MechanicalCartSpeed = 1.25f;

    /// <summary>Boards a minecart on the clicked track piece (or the track underfoot), or climbs out of the one being ridden.</summary>
    private UseOutcome Mount(PlayerEntity p, MinecartParams cfg, Target? target)
    {
        if (p.Minecart != null)
        {
            p.Minecart = null;
            return UseOutcome.Mounted;
        }
        var near = target?.Object is { Def.Track: true } piece ? piece.Anchor : p.FeetCell;
        p.Minecart = Mounts.Minecarts.Board(_world.Session.World, near, p.Aim, cfg);
        if (p.Minecart != null && p.State.MinecartUpgrade) p.Minecart.SpeedScale = MechanicalCartSpeed;
        return p.Minecart != null ? UseOutcome.Mounted : UseOutcome.Blocked;
    }

    /// <summary>Lets a caught critter go on the aimed surface.</summary>
    private UseOutcome Release(PlayerEntity p, ReleaseCritterParams cfg, Target target)
    {
        if (!_world.Session.Content.Enemies.TryGet(cfg.Enemy, out var def)) return UseOutcome.Blocked;
        var cell = target.Cell + target.Normal;
        var feet = new Vector3(cell.X + 0.5f, cell.Y, cell.Z + 0.5f) * Units.CellSize;
        if (_world.Npcs.Spawn(def, feet) == null) return UseOutcome.Blocked;
        ConsumeHeld(p);
        return UseOutcome.Consumed;
    }
}
