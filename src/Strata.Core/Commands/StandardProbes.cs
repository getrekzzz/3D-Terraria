using Strata.Core.Common;
using Strata.Core.Voxels;

namespace Strata.Core.Commands;

/// <summary>Probes available in every context (engine hosts add their own, e.g. <c>player.on_floor</c>).</summary>
internal static class StandardProbes
{
    public static void Register(CommandContext ctx)
    {
        var w = ctx.World;
        ctx.AddProbe("player.life", () => ctx.Player.State.Vitals.Life);
        ctx.AddProbe("player.max_life", () => ctx.Player.State.Vitals.MaxLife);
        ctx.AddProbe("player.mana", () => ctx.Player.State.Vitals.Mana);
        ctx.AddProbe("player.defense", () => ctx.Player.State.Defense);
        ctx.AddProbe("player.deaths", () => ctx.Player.State.Deaths);
        ctx.AddProbe("player.x", () => ctx.Player.Position.X);
        ctx.AddProbe("player.y", () => ctx.Player.Position.Y);
        ctx.AddProbe("player.z", () => ctx.Player.Position.Z);
        ctx.AddProbe("player.speed", () => ctx.Player.Velocity.Length());
        ctx.AddProbe("player.alive", () => ctx.Player.Alive ? 1 : 0);
        ctx.AddProbe("player.cell_y", () => ctx.Player.FeetCell.Y);
        ctx.AddProbe("player.from_mark", () => ctx.Mark is { } m ? System.Numerics.Vector3.Distance(m, ctx.Player.Position) : throw new CommandException("no mark set"));
        ctx.AddProbe("player.held_count", () => ctx.Player.State.Inventory.Held.Count);
        ctx.AddProbe("time.hour", () => w.Session.Clock.Hour);
        ctx.AddProbe("time.day", () => w.Session.Clock.IsDay ? 1 : 0);
        ctx.AddProbe("drops.count", () => w.Drops.Items.Count);
        ctx.AddProbe("chunks.loaded", () => w.Streamer.LoadedCount);
        ctx.AddProbe("chunks.meshed", () => w.Streamer.MeshedCount);
        ctx.AddProbe("stream.idle", () => w.Streamer.IsIdle && w.Jobs.Idle ? 1 : 0);
        ctx.AddProbe("objects.count", () => w.Session.World.TileObjects.Count);
        ctx.AddPrefixProbe("inv.", id => w.Session.Content.Items.TryGet(id, out var item) ? ctx.Player.State.Inventory.Backpack.Count(item) : null);
        ctx.AddPrefixProbe("flag.", id => w.Session.Flags.Contains(id) ? 1 : 0);
        ctx.AddPrefixProbe("buff.", id => ctx.Player.State.Buffs.Has(id) ? 1 : 0);
        // block.<dx>.<dy>.<dz>: runtime block id relative to the player's feet cell (0 = air).
        ctx.AddPrefixProbe("block.", spec =>
        {
            var parts = spec.Split('.');
            if (parts.Length != 3 || !int.TryParse(parts[0], out int dx) || !int.TryParse(parts[1], out int dy) || !int.TryParse(parts[2], out int dz)) return null;
            ushort b = w.Session.World.GetBlock(ctx.Player.FeetCell + new Int3(dx, dy, dz));
            return b == VoxelWorld.Unknown ? -1 : b;
        });
    }

    public static Int3 Cell(System.Numerics.Vector3 meters) =>
        new((int)MathF.Floor(meters.X / Units.CellSize), (int)MathF.Floor(meters.Y / Units.CellSize), (int)MathF.Floor(meters.Z / Units.CellSize));
}
