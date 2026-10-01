using System.Globalization;
using System.Numerics;
using System.Text;
using Strata.Core.Common;
using Strata.Core.Gameplay.Interaction;
using Strata.Core.Gameplay.Items;
using Strata.Core.Simulation;
using Strata.Core.Voxels;

namespace Strata.Core.Commands;

/// <summary>World, player and diagnostic commands available everywhere (console and scripts).</summary>
public static class BuiltinCommands
{
    public static CommandRegistry CreateRegistry()
    {
        var r = new CommandRegistry();
        Register(r);
        EditCommands.Register(r);
        CombatCommands.Register(r);
        LiquidCommands.Register(r);
        TownCommands.Register(r);
        WorldCommands.Register(r);
        PerfCommands.Register(r);
        return r;
    }

    public static void Register(CommandRegistry r)
    {
        r.Register(new CommandDef
        {
            Name = "help", Usage = "help [command]", Help = "Lists commands or describes one.",
            Run = (ctx, a) => CommandResult.Success((ctx.Registry ?? throw new CommandException("no registry")).HelpText(a.OptStr(0))),
        });
        r.Register(new CommandDef
        {
            Name = "probes", Usage = "probes", Help = "Lists values usable by probe/assert.",
            Run = (ctx, _) => CommandResult.Success(string.Join('\n', ctx.ProbeNames)),
        });
        r.Register(new CommandDef
        {
            Name = "probe", Usage = "probe <name>", Help = "Prints a probe value.",
            Run = (ctx, a) => CommandResult.Success($"{a.Str(0, "name")} = {ctx.ReadProbe(a.Str(0, "name")).ToString(CultureInfo.InvariantCulture)}"),
        });
        r.Register(new CommandDef
        {
            Name = "assert", Usage = "assert <probe|number> <op> <probe|number>", Help = "Fails unless the comparison holds (ops: == != < <= > >=).",
            Run = (ctx, a) => Assert(ctx, a),
        });
        r.Register(new CommandDef
        {
            Name = "give", Usage = "give <item> [count]", Help = "Adds items to the backpack.",
            Run = (ctx, a) =>
            {
                var content = ctx.World.Session.Content;
                var id = a.Str(0, "item");
                if (!content.Items.TryGet(id, out var item)) throw new CommandException($"unknown item '{id}'");
                int count = Math.Clamp(a.OptInt(1, 1), 1, 99_999);
                var rest = ctx.Player.State.Inventory.Pickup(new ItemStack(item, count));
                return CommandResult.Success($"Gave {count - rest.Count}x {item.DisplayName}" + (rest.IsEmpty ? "" : $" ({rest.Count} did not fit)"));
            },
        });
        r.Register(new CommandDef
        {
            Name = "clear", Usage = "clear", Help = "Empties the backpack.",
            Run = (ctx, _) =>
            {
                ctx.Player.State.Inventory.Backpack.Clear();
                return CommandResult.Success("Backpack cleared.");
            },
        });
        r.Register(new CommandDef
        {
            Name = "select", Usage = "select <slot 1-10>", Help = "Selects a hotbar slot.",
            Run = (ctx, a) =>
            {
                ctx.Player.State.Inventory.Select(a.Int(0, "slot") - 1);
                return CommandResult.Success($"Holding {ctx.Player.State.Inventory.Held}");
            },
        });
        r.Register(new CommandDef
        {
            Name = "wield", Usage = "wield <item>", Help = "Moves an item from the backpack into the selected hotbar slot and holds it.",
            Run = (ctx, a) =>
            {
                var inv = ctx.Player.State.Inventory;
                var id = a.Str(0, "item");
                int slot = -1;
                for (int i = 0; i < inv.Backpack.Size && slot < 0; i++)
                    if (inv.Backpack[i].Item?.Id == id) slot = i;
                if (slot < 0) throw new CommandException($"no '{id}' in the backpack");
                if (slot < PlayerInventory.HotbarSize) inv.Select(slot);
                else Inventory.Transfer(inv.Backpack, slot, inv.Backpack, inv.SelectedHotbar);
                return CommandResult.Success($"Holding {inv.Held}");
            },
        });
        r.Register(new CommandDef
        {
            Name = "mark", Usage = "mark", Help = "Remembers the player's position (probe player.from_mark).",
            Run = (ctx, _) =>
            {
                ctx.Mark = ctx.Player.Position;
                return CommandResult.Success($"Marked {ctx.Mark}.");
            },
        });
        r.Register(new CommandDef
        {
            Name = "heal", Usage = "heal", Help = "Restores life and mana.",
            Run = (ctx, _) =>
            {
                var v = ctx.Player.State.Vitals;
                v.Life = v.MaxLife;
                v.Mana = v.MaxMana;
                return CommandResult.Success("Healed.");
            },
        });
        r.Register(new CommandDef
        {
            Name = "kill", Usage = "kill", Help = "Kills the player (tests respawn).",
            Run = (ctx, _) =>
            {
                ctx.Player.State.Vitals.Kill();
                return CommandResult.Success("Killed.");
            },
        });
        r.Register(new CommandDef
        {
            Name = "god", Usage = "god [on|off]", Help = "Debug: hits still land but the player cannot die (not saved).",
            Run = (ctx, a) =>
            {
                var v = ctx.Player.State.Vitals;
                v.Undying = (a.OptStr(0) ?? (v.Undying ? "off" : "on")) == "on";
                return CommandResult.Success($"God mode {(v.Undying ? "on" : "off")}.");
            },
        });
        r.Register(new CommandDef
        {
            Name = "tp", Aliases = new[] { "teleport" }, Usage = "tp <x> <y> <z> | tp spawn | tp surface | tp cave <underground|cavern|underworld> | tp landmark <id> | tp zone <biome>",
            Help = "Teleports the player (cell coordinates; ~ or ~n is relative to the feet cell, so tp ~ ~ ~ centres the player in it).",
            Run = Teleport,
        });
        r.Register(new CommandDef
        {
            Name = "biome", Usage = "biome <id>", Help = "Teleports to the nearest surface of a biome.",
            Run = (ctx, a) =>
            {
                var id = a.Str(0, "id");
                var s = ctx.World.Session;
                if (!s.Content.Biomes.Contains(id)) throw new CommandException($"unknown biome '{id}' (known: {string.Join(", ", s.Content.Biomes.All.Select(b => b.Id))})");
                var cell = s.FindSurfaceBiome(id, ctx.Player.FeetCell) ?? throw new CommandException($"no surface '{id}' found");
                ctx.Host.Teleport(ctx.Player, Feet(cell), settle: true);
                return CommandResult.Success($"Teleported to {id} at {cell}.");
            },
        });
        r.Register(new CommandDef
        {
            Name = "time", Usage = "time <hour|dawn|noon|dusk|midnight> | time speed <n>", Help = "Sets the time of day or clock speed.",
            Run = (ctx, a) =>
            {
                var clock = ctx.World.Session.Clock;
                var arg = a.Str(0, "hour");
                if (arg == "speed")
                {
                    clock.Speed = Math.Clamp(a.Int(1, "n"), 1, 3600);
                    return CommandResult.Success($"Clock speed x{clock.Speed}.");
                }
                float hour = arg switch
                {
                    "dawn" => GameClock.DawnHour,
                    "noon" => 12f,
                    "dusk" => GameClock.DuskHour,
                    "midnight" => 0f,
                    _ => a.Float(0, "hour"),
                };
                clock.SetHour(hour);
                return CommandResult.Success($"Time set to {hour:0.0}h ({(clock.IsDay ? "day" : "night")}).");
            },
        });
        r.Register(new CommandDef
        {
            Name = "flag", Aliases = new[] { "progress" }, Usage = "flag <id> [on|off]", Help = "Sets (with its consequences, e.g. hardmode) or clears a world progression flag.",
            Run = (ctx, a) =>
            {
                var s = ctx.World.Session;
                var id = a.Str(0, "id");
                if (!s.Content.Flags.Contains(id)) throw new CommandException($"unknown progression flag '{id}'");
                bool on = (a.OptStr(1) ?? "on") != "off";
                if (on) ctx.World.Progression.Set(id);
                else ctx.World.Progression.Clear(id);
                return CommandResult.Success($"{id} = {(on ? "on" : "off")}");
            },
        });
        r.Register(new CommandDef
        {
            Name = "aim", Usage = "aim <dx> <dy> <dz>", Help = "Points the player's view along a direction.",
            Run = (ctx, a) =>
            {
                var dir = new Vector3(a.Float(0, "dx"), a.Float(1, "dy"), a.Float(2, "dz"));
                if (dir.LengthSquared() < 1e-6f) throw new CommandException("direction must be non-zero");
                ctx.Host.Aim(ctx.Player, dir);
                return CommandResult.Success();
            },
        });
        r.Register(new CommandDef
        {
            Name = "use", Usage = "use", Help = "Uses the held item once at the current aim (ignores the cooldown).",
            Run = (ctx, _) =>
            {
                var p = ctx.Player;
                p.UseCooldown = 0;
                var target = Targeting.Cast(ctx.World.Session.World, p.Eye, p.Aim, 30f);
                var outcome = ctx.World.ItemUse.Use(p, target, freshPress: true);
                return CommandResult.Success($"{outcome}" + (target != null ? $" at {target.Cell}" : ""));
            },
        });
        r.Register(new CommandDef
        {
            Name = "stream", Usage = "stream", Help = "Prints chunk streaming state (diagnostics).",
            Run = (ctx, _) =>
            {
                var s = ctx.World.Streamer;
                var j = ctx.World.Jobs;
                return CommandResult.Success($"loaded {s.LoadedCount} meshed {s.MeshedCount} loads {s.LoadsInFlight} meshes {s.MeshesInFlight} " +
                    $"jobs {j.Pending}/{j.Running} completions {j.Completions.Count} idle {s.IsIdle} | {s.DebugState()}");
            },
        });
        r.Register(new CommandDef
        {
            Name = "save", Usage = "save", Help = "Saves the world and players now.",
            Run = (ctx, _) => CommandResult.Success($"Saved ({ctx.World.SaveAll()} chunks)."),
        });
        r.Register(new CommandDef
        {
            Name = "inspect", Usage = "inspect", Help = "Describes the targeted cell and the player's surroundings.",
            Run = (ctx, _) => CommandResult.Success(Inspect(ctx)),
        });
    }

    private static CommandResult Assert(CommandContext ctx, CommandArgs a)
    {
        double left = ctx.ReadProbe(a.Str(0, "left"));
        string op = a.Str(1, "op");
        double right = ctx.ReadProbe(a.Str(2, "right"));
        bool ok = op switch
        {
            "==" => Math.Abs(left - right) < 1e-6,
            "!=" => Math.Abs(left - right) >= 1e-6,
            "<" => left < right,
            "<=" => left <= right,
            ">" => left > right,
            ">=" => left >= right,
            _ => throw new CommandException($"unknown operator '{op}'"),
        };
        string text = $"{a[0]} ({left.ToString(CultureInfo.InvariantCulture)}) {op} {a[2]} ({right.ToString(CultureInfo.InvariantCulture)})";
        return ok ? CommandResult.Success("ok: " + text) : CommandResult.Fail("ASSERT FAILED: " + text);
    }

    private static CommandResult Teleport(CommandContext ctx, CommandArgs a)
    {
        var s = ctx.World.Session;
        Int3 cell;
        switch (a.Str(0, "x"))
        {
            case "spawn":
                cell = s.SpawnCell;
                break;
            case "surface":
            {
                var here = ctx.Player.FeetCell;
                cell = new Int3(here.X, (int)MathF.Ceiling(s.Generator.Terrain.SurfaceHeight(here.X, here.Z)) + 1, here.Z);
                break;
            }
            case "cave":
            {
                var name = a.Str(1, "layer");
                if (!Enum.TryParse<World.WorldLayer>(name, ignoreCase: true, out var layer) || layer is not (World.WorldLayer.Underground or World.WorldLayer.Cavern or World.WorldLayer.Underworld))
                    throw new CommandException("layer must be underground, cavern or underworld");
                cell = s.FindCave(ctx.Player.FeetCell, layer) ?? throw new CommandException($"no open {name} cave found nearby");
                break;
            }
            case "landmark":
            {
                var id = a.Str(1, "id");
                var marks = s.Generator.Plan.Landmarks;
                cell = marks.TryGetValue(id, out var mark) ? mark : throw new CommandException($"no landmark '{id}' (known: {string.Join(", ", marks.Keys)})");
                break;
            }
            case "zone":
            {
                // Where a world mutation's zone (the radiant wedge, a blight) meets the surface; or inside a planned
                // structure's zone (the Jungle Temple), beside its first object.
                var id = a.Str(1, "biome");
                var zone = s.Mutations.Zones.OfType<WorldGen.Mutations.WedgeStep>().FirstOrDefault(z => z.Zone == id);
                if (zone != null)
                {
                    var (x, z, _, _) = zone.Anchor;
                    cell = new Int3((int)x, (int)MathF.Ceiling(s.Generator.Terrain.SurfaceHeight(x, z)) + 1, (int)z);
                    break;
                }
                // A cave zone (the Aether, a hive): partway out from its middle, settled onto the nearest floor.
                if (s.Generator.Plan.Features.All.OfType<WorldGen.Features.ChamberFeature>().FirstOrDefault(f => f.Zone == id) is { } chamber)
                {
                    cell = new Int3((int)(chamber.Center.X + chamber.Radii.X * 0.6f), (int)(chamber.Center.Y - chamber.Radii.Y * 0.25f), (int)chamber.Center.Z);
                    break;
                }
                var structure = s.Generator.Plan.Features.All.OfType<WorldGen.Structures.StructureFeature>().FirstOrDefault(f => f.Zone == id)
                    ?? throw new CommandException($"no zone '{id}' (mutation zones: {string.Join(", ", s.Mutations.Zones.Select(z => z.Zone))})");
                var inside = structure.Blueprint.Ops.OfType<WorldGen.Structures.ObjectOp>().Select(o => o.Anchor).DefaultIfEmpty(structure.Bounds.Min).Last();
                cell = inside + new Int3(0, 1, 3);
                break;
            }
            default:
            {
                var feet = ctx.Player.FeetCell;
                cell = new Int3(Coordinate(a, 0, "x", feet.X), Coordinate(a, 1, "y", feet.Y), Coordinate(a, 2, "z", feet.Z));
                break;
            }
        }
        if (!s.World.InBounds(cell.X, cell.Y, cell.Z)) throw new CommandException($"{cell} is outside the world");
        // Named surface targets are estimates; exact coordinates and caves are taken as given.
        ctx.Host.Teleport(ctx.Player, Feet(cell), settle: a.Str(0, "x") is "spawn" or "surface" or "landmark" or "zone");
        return CommandResult.Success($"Teleported to {cell}.");
    }

    /// <summary>A cell coordinate: a whole number, or ~ / ~n relative to <paramref name="here"/>.</summary>
    private static int Coordinate(CommandArgs a, int i, string name, int here)
    {
        var text = a.Str(i, name);
        if (!text.StartsWith('~')) return a.Int(i, name);
        if (text.Length == 1) return here;
        return int.TryParse(text.AsSpan(1), NumberStyles.Integer, CultureInfo.InvariantCulture, out int offset)
            ? here + offset
            : throw new CommandException($"<{name}> must be an integer or ~[offset], got '{text}'");
    }

    private static string Inspect(CommandContext ctx)
    {
        var s = ctx.World.Session;
        var p = ctx.Player;
        var cell = p.FeetCell;
        var c = s.ContextAt(cell);
        var sb = new StringBuilder();
        sb.AppendLine($"player cell {cell} biome {string.Join('/', c.Biomes)} layer {c.Layer} depth {c.Depth:0} cells");
        var target = Targeting.Cast(s.World, p.Eye, p.Aim, 30f);
        if (target == null) return sb.Append("target: nothing").ToString();
        var block = s.Content.Blocks[target.Block];
        byte shape = s.World.GetShape(target.Cell.X, target.Cell.Y, target.Cell.Z);
        var flags = s.World.GetFlags(target.Cell.X, target.Cell.Y, target.Cell.Z);
        sb.AppendLine($"target {target.Cell} face {target.Normal} dist {target.Distance:0.0} m: {block.Id} shape {ShapeCode.Shape(shape)}/{ShapeCode.Rotation(shape)} flags {flags}");
        if (target.Object is { } o) sb.AppendLine($"object {o} hp {o.HitPoints}/{o.Def.HitPoints} active {o.Active}");
        return sb.ToString().TrimEnd();
    }

    public static Vector3 Feet(Int3 cell) => new Vector3(cell.X + 0.5f, cell.Y, cell.Z + 0.5f) * Units.CellSize;
}
