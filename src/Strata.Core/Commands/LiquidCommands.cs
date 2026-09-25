using Strata.Core.Common;
using Strata.Core.Content.Defs;
using Strata.Core.Voxels;

namespace Strata.Core.Commands;

/// <summary>Liquid commands: pour or clear liquid (coordinates relative to the player's feet) and drive the simulation.</summary>
public static class LiquidCommands
{
    public static void Register(CommandRegistry r)
    {
        r.Register(new CommandDef
        {
            Name = "liquid", Usage = "liquid <water|lava|honey|none> <dx> <dy> <dz> [<dx1> <dy1> <dz1>] [level]",
            Help = "Sets liquid in one cell or a box (open cells only). Level 1–31, default full.",
            Run = (ctx, a) =>
            {
                if (a.Count is not (4 or 5 or 7 or 8)) throw new CommandException("expected a cell or a box, then an optional level");
                var name = a.Str(0, "liquid");
                LiquidDef? liquid = null;
                if (name != "none" && !ctx.World.Session.Content.Liquids.TryGet(name, out liquid))
                    throw new CommandException($"unknown liquid '{name}'");
                var here = ctx.Player.FeetCell;
                var p0 = here + new Int3(a.Int(1, "dx"), a.Int(2, "dy"), a.Int(3, "dz"));
                var p1 = a.Count >= 7 ? here + new Int3(a.Int(4, "dx1"), a.Int(5, "dy1"), a.Int(6, "dz1")) : p0;
                int level = a.Count is 5 or 8 ? a.Int(a.Count - 1, "level") : LiquidCode.MaxLevel;
                if (level is < 1 or > LiquidCode.MaxLevel) throw new CommandException($"level must be 1–{LiquidCode.MaxLevel}");
                var min = Int3.Min(p0, p1);
                var max = Int3.Max(p0, p1);
                long volume = (long)(max.X - min.X + 1) * (max.Y - min.Y + 1) * (max.Z - min.Z + 1);
                if (volume > EditCommands.MaxFillCells) throw new CommandException($"box of {volume} cells exceeds {EditCommands.MaxFillCells}");
                int n = ctx.World.Editor.FillLiquid(min, max, liquid, level);
                return CommandResult.Success($"Set {n} cells to {liquid?.Id ?? "none"}.");
            },
        });
        r.Register(new CommandDef
        {
            Name = "liquids", Usage = "liquids [on|off|settle <rounds>]",
            Help = "Shows liquid simulation state, pauses/resumes it, or runs flow rounds immediately.",
            Run = (ctx, a) =>
            {
                var sim = ctx.World.Liquids;
                switch (a.OptStr(0))
                {
                    case null: break;
                    case "on": sim.Enabled = true; break;
                    case "off": sim.Enabled = false; break;
                    case "settle":
                        int rounds = sim.Settle(Math.Clamp(a.OptInt(1, 100), 1, 10_000));
                        return CommandResult.Success($"Ran {rounds} rounds; {sim.ActiveCells} cells still active.");
                    default: throw new CommandException("expected on, off or settle");
                }
                return CommandResult.Success($"Liquids {(sim.Enabled ? "on" : "paused")}: {sim.ActiveCells} active cells, {sim.Steps} steps, last step changed {sim.LastChanged}.");
            },
        });
    }
}
