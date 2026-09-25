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
