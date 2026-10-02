using Strata.Core.Common;
using Strata.Core.Voxels;

namespace Strata.Core.Commands;

/// <summary>
/// Probes available in every context. Feature probes live in their own <see cref="ProbeSourceAttribute"/> classes
/// (<see cref="TownProbes"/>); engine hosts add theirs the same way (e.g. <c>player.on_floor</c>).
/// </summary>
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
        ctx.AddProbe("player.minecart", () => ctx.Player.Minecart != null ? 1 : 0);
        ctx.AddProbe("player.hook", () => (int)ctx.Player.Hook.State); // 0 none, 1 flying, 2 latched, 3 returning
        ctx.AddProbe("wires.count", () => w.Wires.All.Count);
        ctx.AddProbe("player.alive", () => ctx.Player.Alive ? 1 : 0);
        ctx.AddProbe("player.cell_y", () => ctx.Player.FeetCell.Y);
        ctx.AddProbe("player.from_mark", () => ctx.Mark is { } m ? System.Numerics.Vector3.Distance(m, ctx.Player.Position) : throw new CommandException("no mark set"));
        ctx.AddProbe("player.held_count", () => ctx.Player.State.Inventory.Held.Count);
        ctx.AddProbe("player.hotbar", () => ctx.Player.State.Inventory.SelectedHotbar); // 0-based
        ctx.AddProbe("time.hour", () => w.Session.Clock.Hour);
        ctx.AddProbe("time.day", () => w.Session.Clock.IsDay ? 1 : 0);
        ctx.AddProbe("time.scale", () => w.TimeScale);
        ctx.AddProbe("player.sleeping", () => ctx.Player.Sleeping ? 1 : 0);
        ctx.AddProbe("drops.count", () => w.Drops.Items.Count);
        ctx.AddPrefixProbe("drops.", id => w.Session.Content.Items.Contains(id) ? w.Drops.Items.Where(d => d.Stack.Item?.Id == id).Sum(d => d.Stack.Count) : null);
        ctx.AddProbe("chunks.loaded", () => w.Streamer.LoadedCount);
        ctx.AddProbe("chunks.meshed", () => w.Streamer.MeshedCount);
        ctx.AddProbe("stream.idle", () => w.Streamer.IsIdle && w.Jobs.Idle ? 1 : 0);
        ctx.AddProbe("save.in_flight", () => w.Session.Saver is { InFlight: true } || w.AutosaveInFlight ? 1 : 0);
        ctx.AddProbe("objects.count", () => w.Session.World.TileObjects.Count);
        ctx.AddProbe("npcs.count", () => w.Npcs.All.Count);
        ctx.AddProbe("npcs.alive_life", () => w.Npcs.All.Sum(n => n.Life));
        ctx.AddProbe("projectiles.count", () => w.Projectiles.All.Count);
        ctx.AddProbe("pets.active", () => w.Projectiles.PetCount(lights: false));
        ctx.AddProbe("pets.lights", () => w.Projectiles.PetCount(lights: true));
        ctx.AddProbe("melee.swings", () => w.Melee.ActiveSwings);
        // The last melee hit only (any player, any creature; a later hit overwrites it): the blade's curve angle (0 ahead, 90 up; -999 for a thrust or before any hit) and the
        // struck creature's distance from the swing pivot or thrust origin (m; -1 before any hit).
        ctx.AddProbe("melee.hit_deg", () => w.Melee.LastHit is { BladeDeg: var d } && !float.IsNaN(d) ? d : -999);
        ctx.AddProbe("melee.hit_dist", () => w.Melee.LastHit?.Distance ?? -1);
        ctx.AddProbe("liquid.active", () => w.Liquids.ActiveCells);
        ctx.AddProbe("house.valid", () => w.Housing.Scan(ctx.Player.FeetCell + Int3.Up).Valid ? 1 : 0);
        ctx.AddProbe("liquid.steps", () => w.Liquids.Steps);
        // Voxel light at the player's head (0–15; -1 before the chunk is lit) and the sky map's column top.
        ctx.AddProbe("light.sky", () => HeadLight(w, ctx, block: false));
        ctx.AddProbe("light.block", () => HeadLight(w, ctx, block: true));
        ctx.AddProbe("sky.top", () => w.Session.Sky.SkyTop(ctx.Player.FeetCell.X, ctx.Player.FeetCell.Z));
        ctx.AddProbe("mutation.level", () => w.Session.Mutations.Level);
        ctx.AddProbe("mutation.behind", () => w.Mutations.Behind);
        ctx.AddProbe("spread.conversions", () => w.Spread.Conversions);
        ctx.AddProbe("player.in_liquid", () => ctx.Player.Liquid?.Code ?? 0);
        ctx.AddProbe("player.submerged", () => ctx.Player.Submerged ? 1 : 0);
        ctx.AddProbe("player.breath", () => ctx.Player.Breath);
        ctx.AddPrefixProbe("npcs.", id => w.Session.Content.Enemies.Contains(id) ? w.Npcs.All.Count(n => n.Def.Id == id) : null);
        ctx.AddProbe("inv.trash", () => ctx.Player.State.Inventory.Trash[0].Count);
        ctx.AddProbe("inv.cursor", () => ctx.Player.State.Inventory.Cursor.Count);
        // The accessory slots the world and a Demon Heart unlock, and those hidden on the character (a bit each).
        ctx.AddProbe("inv.accessory_slots", () => ctx.Player.State.Inventory.AccessorySlotsUnlocked);
        ctx.AddProbe("inv.hidden_accessories", () => ctx.Player.State.Inventory.HiddenAccessories);
        // The purse in copper: coins in the coin slots and the backpack.
        ctx.AddProbe("inv.coins", () => Gameplay.Items.Coins.Total(ctx.Player.State.Inventory));
        // inv.<item>: in the backpack, coin, ammo and trash slots (worn items do not count).
        ctx.AddPrefixProbe("inv.", id => w.Session.Content.Items.TryGet(id, out var item)
            ? ctx.Player.State.Inventory.CountAll(item) + ctx.Player.State.Inventory.Trash.Count(item)
            : null);
        ctx.AddPrefixProbe("event.", id => w.Session.Content.Events.Contains(id) ? (w.Events.IsActive(id) ? 1 : 0) : null);
        ctx.AddPrefixProbe("flag.", id => w.Session.Flags.Contains(id) ? 1 : 0);
        ctx.AddPrefixProbe("buff.", id => ctx.Player.State.Buffs.Has(id) ? 1 : 0);
        // zone.<biome>: 1 while the player stands in a zone (the crypt, spider nests…).
        ctx.AddPrefixProbe("zone.", id => w.Session.Content.Biomes.Contains(id) ? (w.Session.ContextAt(ctx.Player.FeetCell).Biomes.Contains(id) ? 1 : 0) : null);
        // near.<block>: cells of a block within 8 cells of the player's feet.
        ctx.AddPrefixProbe("near.", id =>
        {
            if (!w.Session.Content.Blocks.TryGet(id, out var def)) return null;
            var f = ctx.Player.FeetCell;
            int n = 0;
            for (int y = -8; y <= 8; y++)
                for (int z = -8; z <= 8; z++)
                    for (int x = -8; x <= 8; x++)
                        if (w.Session.World.GetBlock(f.X + x, f.Y + y, f.Z + z) == def.RuntimeId) n++;
            return n;
        });
        // liquid.<dx>.<dy>.<dz>: liquid level relative to the player's feet cell (0 = dry).
        ctx.AddPrefixProbe("liquid.", spec =>
        {
            var parts = spec.Split('.');
            if (parts.Length != 3 || !int.TryParse(parts[0], out int dx) || !int.TryParse(parts[1], out int dy) || !int.TryParse(parts[2], out int dz)) return null;
            var c = ctx.Player.FeetCell + new Int3(dx, dy, dz);
            return LiquidCode.Level(w.Session.World.GetLiquid(c.X, c.Y, c.Z));
        });
        // block.<dx>.<dy>.<dz>: runtime block id relative to the player's feet cell (0 = air).
        ctx.AddPrefixProbe("block.", spec =>
        {
            var parts = spec.Split('.');
            if (parts.Length != 3 || !int.TryParse(parts[0], out int dx) || !int.TryParse(parts[1], out int dy) || !int.TryParse(parts[2], out int dz)) return null;
            ushort b = w.Session.World.GetBlock(ctx.Player.FeetCell + new Int3(dx, dy, dz));
            return b == VoxelWorld.Unknown ? -1 : b;
        });
    }

    private static double HeadLight(Session.GameWorld w, CommandContext ctx, bool block)
    {
        var cell = ctx.Player.FeetCell + Int3.Up;
        var chunk = w.Session.World.GetChunk(ChunkMath.CellToChunk(cell));
        if (chunk?.Light == null) return -1;
        ushort v = chunk.Light[ChunkMath.Index(ChunkMath.CellToLocal(cell))];
        return block ? Meshing.LightValue.BlockMax(v) : Meshing.LightValue.Sky(v);
    }

    public static Int3 Cell(System.Numerics.Vector3 meters) =>
        new((int)MathF.Floor(meters.X / Units.CellSize), (int)MathF.Floor(meters.Y / Units.CellSize), (int)MathF.Floor(meters.Z / Units.CellSize));
}
