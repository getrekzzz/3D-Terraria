using System.Numerics;
using Strata.Core.Common;

namespace Strata.Core.Commands;

/// <summary>Creature and combat commands.</summary>
public static class CombatCommands
{
    /// <summary>Nearest standable position in the column of <paramref name="at"/> (searching up and down 12 cells).</summary>
    private static Vector3 Ground(CommandContext ctx, Vector3 at)
    {
        var s = ctx.World.Session;
        int x = (int)MathF.Floor(at.X / Units.CellSize), y = (int)MathF.Floor(at.Y / Units.CellSize), z = (int)MathF.Floor(at.Z / Units.CellSize);
        for (int d = 0; d <= 12; d++)
            foreach (int sign in new[] { 1, -1 })
                if (s.IsStandable(x, y + d * sign, z, 3)) return new Vector3(at.X, (y + d * sign) * Units.CellSize, at.Z);
        return at;
    }

    public static void Register(CommandRegistry r)
    {
        r.Register(new CommandDef
        {
            Name = "spawn", Usage = "spawn <enemy> [count] [distance]", Help = "Spawns creatures in front of the player (distance in metres, default 5).",
            Run = (ctx, a) =>
            {
                var content = ctx.World.Session.Content;
                var id = a.Str(0, "enemy");
                if (!content.Enemies.TryGet(id, out var def)) throw new CommandException($"unknown enemy '{id}' (known: {string.Join(", ", content.Enemies.All.Select(e => e.Id))})");
                int count = Math.Clamp(a.OptInt(1, 1), 1, 50);
                float distance = a.Count > 2 ? a.Float(2, "distance") : 5f;
                var p = ctx.Player;
                var flat = Vector3.Normalize(new Vector3(p.Aim.X, 0, p.Aim.Z) + new Vector3(0, 0, 1e-4f));
                int spawned = 0;
                for (int i = 0; i < count; i++)
                {
                    var side = new Vector3(-flat.Z, 0, flat.X) * (i - (count - 1) / 2f) * 1.2f;
                    var feet = Ground(ctx, p.Position + flat * distance + side) + new Vector3(0, def.Gravity ? 0.05f : 2f, 0);
                    if (ctx.World.Npcs.Spawn(def, feet) != null) spawned++;
                }
                return CommandResult.Success($"Spawned {spawned}x {def.DisplayName}.");
            },
        });
        r.Register(new CommandDef
        {
            Name = "event", Usage = "event <id> | event stop <id> | event list", Help = "Starts, stops or lists world events.",
            Run = (ctx, a) =>
            {
                var events = ctx.World.Events;
                var content = ctx.World.Session.Content;
                switch (a.Str(0, "id"))
                {
                    case "list":
                        return CommandResult.Success("active: " + (events.Active.Count == 0 ? "none" : string.Join(", ", events.Active.Select(e => $"{e.Def.Id} ({(e.TicksLeft < 0 ? "until phase end" : $"{e.TicksLeft / 60}s")}, kills {e.Kills})")))
                            + "\nknown: " + string.Join(", ", content.Events.All.Select(e => e.Id)));
                    case "stop":
                        var stopId = a.Str(1, "id");
                        return events.Stop(stopId) ? CommandResult.Success($"Stopped {stopId}.") : CommandResult.Fail($"{stopId} is not active");
                    default:
                        var id = a[0];
                        if (!content.Events.TryGet(id, out var def)) throw new CommandException($"unknown event '{id}'");
                        return events.Start(def) ? CommandResult.Success($"Started {def.DisplayName}.") : CommandResult.Fail($"{id} is already active");
                }
            },
        });
        r.Register(new CommandDef
        {
            Name = "boss", Usage = "boss <enemy> [distance]", Help = "Summons a boss near the player, ignoring its summon conditions.",
            Run = (ctx, a) =>
            {
                var content = ctx.World.Session.Content;
                var id = a.Str(0, "enemy");
                if (!content.Enemies.TryGet(id, out var def) || def.Boss == null)
                    throw new CommandException($"unknown boss '{id}' (bosses: {string.Join(", ", content.Enemies.All.Where(e => e.Boss != null).Select(e => e.Id))})");
                float distance = a.Count > 1 ? a.Float(1, "distance") : 20f;
                var p = ctx.Player;
                var flat = Vector3.Normalize(new Vector3(p.Aim.X, 0, p.Aim.Z) + new Vector3(0, 0, 1e-4f));
                var at = p.Position + flat * distance + new Vector3(0, def.Gravity ? 6f : def.Boss.Segments > 0 ? -8f : 8f, 0);
                return ctx.World.Npcs.Spawn(def, at) is { } npc ? CommandResult.Success($"{def.Boss.Title} summoned (id {npc.Id}).") : CommandResult.Fail("creature cap reached");
            },
        });
        r.Register(new CommandDef
        {
            Name = "slay", Usage = "slay [<enemy id>]", Help = "Kills creatures (all, or one kind) as if in battle: loot drops and bosses count as defeated.",
            Run = (ctx, a) =>
            {
                var id = a.OptStr(0);
                if (id != null && !ctx.World.Session.Content.Enemies.Contains(id)) throw new CommandException($"unknown enemy '{id}'");
                var victims = ctx.World.Npcs.All.Where(n => n.Alive && (id == null || n.Def.Id == id)).ToList();
                foreach (var n in victims) ctx.World.Npcs.Kill(n);
                return CommandResult.Success($"Slew {victims.Count} creatures.");
            },
        });
        r.Register(new CommandDef
        {
            Name = "killall", Usage = "killall", Help = "Removes every creature (no loot).",
            Run = (ctx, _) =>
            {
                int n = ctx.World.Npcs.All.Count;
                ctx.World.Npcs.Clear();
                return CommandResult.Success($"Removed {n} creatures.");
            },
        });
        r.Register(new CommandDef
        {
            Name = "equip", Usage = "equip <item>", Help = "Gives an item and equips it (armor, accessory, vanity, dye, pet, mount or hook).",
            Run = (ctx, a) =>
            {
                var content = ctx.World.Session.Content;
                var id = a.Str(0, "item");
                if (!content.Items.TryGet(id, out var item)) throw new CommandException($"unknown item '{id}'");
                var inv = ctx.Player.State.Inventory;
                if (!inv.Pickup(new Gameplay.Items.ItemStack(item, 1)).IsEmpty) throw new CommandException("the backpack is full");
                int slot = -1;
                for (int i = 0; i < inv.Backpack.Size; i++)
                    if (inv.Backpack[i].Item == item) slot = i;
                if (slot < 0 || !inv.Equip(slot)) throw new CommandException($"'{id}' cannot be equipped");
                return CommandResult.Success($"Equipped {item.DisplayName}.");
            },
        });
        r.Register(new CommandDef
        {
            Name = "ai", Usage = "ai <on|off>", Help = "Debug: freezes every creature in place (off) or lets them move again (on).",
            Run = (ctx, a) =>
            {
                ctx.World.Npcs.Paused = a.Str(0, "mode") switch
                {
                    "off" => true,
                    "on" => false,
                    _ => throw new CommandException("expected on or off"),
                };
                return CommandResult.Success(ctx.World.Npcs.Paused ? "Creatures frozen." : "Creatures moving.");
            },
        });
        r.Register(new CommandDef
        {
            Name = "spawning", Usage = "spawning <on|off|now>", Help = "Toggles natural spawning or forces one spawn attempt.",
            Run = (ctx, a) =>
            {
                var s = ctx.World.Spawner;
                switch (a.Str(0, "mode"))
                {
                    case "on": s.Enabled = true; return CommandResult.Success("Natural spawning on.");
                    case "off": s.Enabled = false; return CommandResult.Success("Natural spawning off.");
                    case "now":
                        var list = s.TrySpawnNear(ctx.Player);
                        return CommandResult.Success(list.Count == 0 ? "No valid spawn found." : $"Spawned {string.Join(", ", list.Select(n => n.Def.Id))}.");
                    default: throw new CommandException("expected on, off or now");
                }
            },
        });
    }
}
