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
            Name = "fill", Usage = "fill <dx0> <dy0> <dz0> <dx1> <dy1> <dz1> <block|air>",
            Help = "Fills a box relative to the player's feet.",
            Run = (ctx, a) =>
            {
                var here = Here(ctx);
                var p0 = here + new Int3(a.Int(0, "dx0"), a.Int(1, "dy0"), a.Int(2, "dz0"));
                var p1 = here + new Int3(a.Int(3, "dx1"), a.Int(4, "dy1"), a.Int(5, "dz1"));
                var (id, def) = Block(ctx, a.Str(6, "block"));
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
                            batch.SetBlock(x, y, z, id, 0, id == 0 ? CellFlags.None : CellFlags.PlayerPlaced);
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
    }

    private static Int3 Here(CommandContext ctx) => ctx.Player.FeetCell;

    private static (ushort Id, BlockDef? Def) Block(CommandContext ctx, string name)
    {
        if (name is "air" or "0") return (0, null);
        var content = ctx.World.Session.Content;
        if (!content.Blocks.TryGet(name, out var def)) throw new CommandException($"unknown block '{name}'");
        return ((ushort)def.RuntimeId, def);
    }
}
