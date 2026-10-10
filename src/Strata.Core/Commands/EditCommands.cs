using Strata.Core.Common;
using Strata.Core.Content.Defs;
using Strata.Core.Gameplay.Building;
using Strata.Core.Voxels;

namespace Strata.Core.Commands;

/// <summary>World editing commands (coordinates relative to the player's feet cell).</summary>
public static class EditCommands
{
    public const int MaxFillCells = 64 * 64 * 64;

    public static void Register(CommandRegistry r)
    {
        r.Register(new CommandDef
        {
            Name = "drop", Usage = "drop <item> [count] [<dx> <dy> <dz> [pickup delay ticks]]",
            Help = "Drops an item stack into the world at a cell relative to the feet (default: in front), out of pickup reach for a while (600 ticks unless given).",
            Run = (ctx, a) =>
            {
                var content = ctx.World.Session.Content;
                var item = content.Items.TryGet(a.Str(0, "item"), out var found) ? found : throw new CommandException($"unknown item '{a.Str(0, "item")}'");
                int count = Math.Clamp(a.OptInt(1, 1), 1, item.MaxStack);
                var offset = a.Count >= 5 ? new Int3(a.Int(2, "dx"), a.Int(3, "dy"), a.Int(4, "dz")) : new Int3(0, 1, 3);
                var cell = Here(ctx) + offset;
                var at = new System.Numerics.Vector3(cell.X + 0.5f, cell.Y + 0.5f, cell.Z + 0.5f) * Units.CellSize;
                int delay = Math.Max(0, a.OptInt(5, 600));
                ctx.World.Drops.Spawn(new Gameplay.Items.ItemStack(item, count), at, System.Numerics.Vector3.Zero, pickupDelay: delay);
                return CommandResult.Success($"Dropped {count} {item.DisplayName} at {cell}.");
            },
        });
        r.Register(new CommandDef
        {
            Name = "setblock", Usage = "setblock <dx> <dy> <dz> <block|air> [shape] [rotation]",
            Help = "Sets one cell relative to the player's feet.",
            Run = (ctx, a) =>
            {
                var cell = Here(ctx) + new Int3(a.Int(0, "dx"), a.Int(1, "dy"), a.Int(2, "dz"));
                var (id, block) = Block(ctx, a.Str(3, "block"));
                byte shape = 0;
                if (a.OptStr(4) is { } shapeName)
                {
                    if (!Enum.TryParse<CellShape>(shapeName, true, out var s)) throw new CommandException($"unknown shape '{shapeName}'");
                    shape = ShapeCode.Pack(s, a.OptInt(5, 0));
                }
                ctx.World.Editor.SetBlock(cell, id, shape, id == 0 ? CellFlags.None : CellFlags.PlayerPlaced);
                return CommandResult.Success($"{cell} = {block?.Id ?? "air"}");
            },
        });
        r.Register(new CommandDef
        {
            Name = "fill", Usage = "fill <dx0> <dy0> <dz0> <dx1> <dy1> <dz1> <block|air> [natural]",
            Help = "Fills a box relative to the player's feet. Blocks are placed by hand (crisp) unless 'natural' is given: "
                + "then they are unflagged like generated terrain and mesh smooth.",
            Run = (ctx, a) =>
            {
                var here = Here(ctx);
                var p0 = here + new Int3(a.Int(0, "dx0"), a.Int(1, "dy0"), a.Int(2, "dz0"));
                var p1 = here + new Int3(a.Int(3, "dx1"), a.Int(4, "dy1"), a.Int(5, "dz1"));
                var (id, def) = Block(ctx, a.Str(6, "block"));
                bool natural = a.OptStr(7) switch
                {
                    null => false,
                    "natural" => true,
                    var other => throw new CommandException($"unknown fill option '{other}' (expected 'natural')"),
                };
                var flags = id == 0 || natural ? CellFlags.None : CellFlags.PlayerPlaced;
                bool displaces = def is { IsSolid: true };
                var min = Int3.Min(p0, p1);
                var max = Int3.Max(p0, p1);
                long volume = (long)(max.X - min.X + 1) * (max.Y - min.Y + 1) * (max.Z - min.Z + 1);
                if (volume > MaxFillCells) throw new CommandException($"box of {volume} cells exceeds {MaxFillCells}");
                var batch = ctx.World.Session.World.BeginEdit();
                for (int y = min.Y; y <= max.Y; y++)
                    for (int z = min.Z; z <= max.Z; z++)
                        for (int x = min.X; x <= max.X; x++)
                        {
                            batch.SetBlock(x, y, z, id, 0, flags);
                            if (natural && id != 0) batch.ClearFlags(x, y, z); // a hand-placed block it replaces was flagged
                            if (displaces) batch.SetLiquid(x, y, z, 0);
                        }
                int changed = batch.PendingChanges;
                ctx.World.Streamer.MarkDirty(ctx.World.Session.Commit(batch));
                // Objects inside the box are replaced by the fill (and would otherwise float in cleared space).
                var box = new Aabb3i(min, max + Int3.One);
                var store = ctx.World.Session.World.TileObjects;
                var inside = store.InBox(box).ToList();
                foreach (var obj in inside) ctx.World.Editor.BreakObject(obj, dropItems: false);
                return CommandResult.Success($"Filled {changed} cells, removed {inside.Count} objects.");
            },
        });
        r.Register(new CommandDef
        {
            Name = "place", Usage = "place <object> <dx> <dy> <dz> [rotation]",
            Help = "Places a tile object with its minimum corner relative to the player's feet.",
            Run = (ctx, a) =>
            {
                var content = ctx.World.Session.Content;
                var id = a.Str(0, "object");
                if (!content.TileObjects.TryGet(id, out var def)) throw new CommandException($"unknown tile object '{id}'");
                var anchor = Here(ctx) + new Int3(a.Int(1, "dx"), a.Int(2, "dy"), a.Int(3, "dz"));
                var result = ctx.World.Editor.PlaceObject(def, anchor, a.OptInt(4, 0));
                return result == PlacementResult.Ok ? CommandResult.Success($"Placed {id} at {anchor}.") : CommandResult.Fail($"Cannot place {id} at {anchor}: {result}");
            },
        });
        r.Register(new CommandDef
        {
            Name = "regen", Usage = "regen [radius]", Help = "Regenerates the chunks around the player from the world seed (discards edits).",
            Run = (ctx, a) =>
            {
                int radius = Math.Clamp(a.OptInt(0, 0), 0, 3);
                var center = ChunkMath.CellToChunk(Here(ctx));
                var dirty = new HashSet<Int3>();
                int count = 0;
                for (int y = -radius; y <= radius; y++)
                    for (int z = -radius; z <= radius; z++)
                        for (int x = -radius; x <= radius; x++)
                        {
                            var c = center + new Int3(x, y, z);
                            if (!ctx.World.Session.World.HasChunk(c)) continue;
                            dirty.UnionWith(ctx.World.Session.Regenerate(c));
                            count++;
                        }
                ctx.World.Streamer.MarkDirty(dirty);
                return CommandResult.Success($"Regenerated {count} chunks.");
            },
        });
        r.Register(new CommandDef
        {
            Name = "wire", Usage = "wire <dx0> <dy0> <dz0> <dx1> <dy1> <dz1> [red|blue|green|yellow|actuator|cut]",
            Help = "Lays wire of a colour (default red) or actuators through a box relative to the player's feet; 'cut' removes all wiring there.",
            Run = (ctx, a) =>
            {
                var here = Here(ctx);
                var p0 = here + new Int3(a.Int(0, "dx0"), a.Int(1, "dy0"), a.Int(2, "dz0"));
                var p1 = here + new Int3(a.Int(3, "dx1"), a.Int(4, "dy1"), a.Int(5, "dz1"));
                var min = Int3.Min(p0, p1);
                var max = Int3.Max(p0, p1);
                if ((long)(max.X - min.X + 1) * (max.Y - min.Y + 1) * (max.Z - min.Z + 1) > MaxFillCells) throw new CommandException("box too large");
                string mode = a.OptStr(6) ?? "red";
                var color = mode is "cut" or "actuator" ? Gameplay.Wiring.WireColor.None : ParseColor(mode);
                var wires = ctx.World.Wires;
                int n = 0;
                for (int y = min.Y; y <= max.Y; y++)
                    for (int z = min.Z; z <= max.Z; z++)
                        for (int x = min.X; x <= max.X; x++)
                        {
                            var c = new Int3(x, y, z);
                            if (mode == "cut") n += wires.Cut(c) > 0 ? 1 : 0;
                            else if (mode == "actuator") n += wires.PlaceActuator(c) ? 1 : 0;
                            else n += wires.Place(c, color) ? 1 : 0;
                        }
                return CommandResult.Success(mode switch { "cut" => $"Cut wiring in {n} cells.", "actuator" => $"Placed {n} actuators.", _ => $"Laid {mode} wire in {n} cells." });
            },
        });
        r.Register(new CommandDef
        {
            Name = "paint", Usage = "paint <dx0> <dy0> <dz0> <dx1> <dy1> <dz1> <paint item|none>", Help = "Paints the blocks in a box relative to the player's feet.",
            Run = (ctx, a) =>
            {
                var here = Here(ctx);
                var p0 = here + new Int3(a.Int(0, "dx0"), a.Int(1, "dy0"), a.Int(2, "dz0"));
                var p1 = here + new Int3(a.Int(3, "dx1"), a.Int(4, "dy1"), a.Int(5, "dz1"));
                string id = a.Str(6, "paint");
                int paint = id == "none" ? 0 : ctx.World.Session.Content.Items.TryGet(id, out var item) && item.Paint > 0 ? item.Paint
                    : throw new CommandException($"'{id}' is not a paint");
                var min = Int3.Min(p0, p1);
                var max = Int3.Max(p0, p1);
                int n = 0;
                for (int y = min.Y; y <= max.Y; y++)
                    for (int z = min.Z; z <= max.Z; z++)
                        for (int x = min.X; x <= max.X; x++)
                            if (ctx.World.Editor.PaintBlock(new Int3(x, y, z), paint)) n++;
                return CommandResult.Success($"Painted {n} blocks.");
            },
        });
        r.Register(new CommandDef
        {
            Name = "interact", Usage = "interact <dx> <dy> <dz> | interact nearest <objectId> [radius] [expectedOutcome]",
            Help = "Interacts with the tile object at a cell relative to the player's feet (doors, levers), or with the nearest object of a kind within a radius (default 8 cells); fails when the outcome is not the expected one (e.g. refused, containeropened).",
            Run = (ctx, a) =>
            {
                if (a.Str(0, "dx") == "nearest") return InteractNearest(ctx, a.Str(1, "objectId"), a.OptInt(2, 8), a.Count > 3 ? a.Str(3, "expectedOutcome") : null);
                var cell = Here(ctx) + new Int3(a.Int(0, "dx"), a.Int(1, "dy"), a.Int(2, "dz"));
                var obj = ctx.World.Session.World.TileObjects.At(cell) ?? throw new CommandException($"no object at {cell}");
                return CommandResult.Success($"{obj.Def.Id}: {ctx.World.Interaction.Interact(ctx.Player, obj)}");
            },
        });
    }

    private static Int3 Here(CommandContext ctx) => ctx.Player.FeetCell;

    private static CommandResult InteractNearest(CommandContext ctx, string id, int radius, string? expect)
    {
        var here = Here(ctx);
        var box = new Aabb3i(here - Int3.One * radius, here + Int3.One * (radius + 1));
        Voxels.TileObjects.TileObjectInstance? best = null;
        long bestDist = long.MaxValue;
        foreach (var o in ctx.World.Session.World.TileObjects.InBox(box))
        {
            if (o.Def.Id != id) continue;
            var d = o.Anchor - here;
            long dist = (long)d.X * d.X + (long)d.Y * d.Y + (long)d.Z * d.Z;
            if (dist < bestDist || (dist == bestDist && o.Id < best!.Id)) (best, bestDist) = (o, dist);
        }
        if (best == null) throw new CommandException($"no {id} within {radius} cells of {here}");
        var outcome = ctx.World.Interaction.Interact(ctx.Player, best);
        var text = $"{best.Def.Id}@{best.Anchor}: {outcome}";
        if (expect != null && !string.Equals(expect, outcome.ToString(), StringComparison.OrdinalIgnoreCase))
            throw new CommandException($"{text}, expected {expect}");
        return CommandResult.Success(text);
    }

    private static (ushort Id, BlockDef? Def) Block(CommandContext ctx, string name)
    {
        if (name is "air" or "0") return (0, null);
        var content = ctx.World.Session.Content;
        if (!content.Blocks.TryGet(name, out var def)) throw new CommandException($"unknown block '{name}'");
        return ((ushort)def.RuntimeId, def);
    }

    private static Gameplay.Wiring.WireColor ParseColor(string name)
    {
        try { return Gameplay.Wiring.WireColors.Parse(name); }
        catch (ArgumentException e) { throw new CommandException(e.Message); }
    }
}
