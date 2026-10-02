using System.Numerics;
using Strata.Core.Common;
using Strata.Core.Content;
using Strata.Core.Diagnostics;
using Strata.Core.Gameplay.Items;
using Strata.Core.Gameplay.Player;
using Strata.Core.Meshing;
using Strata.Core.Session;
using Strata.Core.Streaming;
using Strata.Core.World;

namespace Strata.Core.Tests.Support;

/// <summary>
/// A headless <see cref="GameWorld"/> for gameplay tests: the chunks around spawn are generated and installed
/// synchronously and a flat stone arena is carved so tests do not depend on terrain details.
/// </summary>
public sealed class TestGame : IDisposable
{
    public const int ArenaRadius = 12;
    public const int ArenaHeight = 12;

    private static readonly RenderTables Tables = new(TestPaths.Content, _ => 0);

    private TestGame(GameWorld world, Int3 floor)
    {
        World = world;
        Floor = floor;
    }

    public GameWorld World { get; }

    /// <summary>The top floor cell at the arena centre (stand on <c>Floor + Up</c>).</summary>
    public Int3 Floor { get; }

    public MemoryLog Log => (MemoryLog)World.Session.Log;
    public PlayerEntity Player => World.LocalPlayer!;
    public Strata.Core.Content.ContentDatabase Content => World.Session.Content;

    public static TestGame Create(string? saveDir = null, long seed = 42, Difficulty difficulty = Difficulty.Classic, ContentDatabase? content = null,
        WorldSizePreset size = WorldSizePreset.Tiny, SecretSeed secret = SecretSeed.None)
    {
        var log = new MemoryLog();
        var settings = new WorldSettings { Name = "test", Seed = seed, Size = size, Difficulty = difficulty, Secret = secret };
        var session = WorldSession.Create(content ?? TestPaths.Content, settings, saveDir, content == null ? Tables : new RenderTables(content, _ => 0), log);
        session.Seasons.Clock = () => new DateTime(2026, 6, 1); // no seasonal event unless a test forces one
        var world = new GameWorld(session, new StreamingSettings { MeshRadius = 1, MeshDown = 1, MeshUp = 1, SkyColumnRadius = 1 }, 1, saveDir == null ? null : Path.Combine(saveDir, "players"));
        world.ItemUse.Timing = TestPaths.Timing; // the shipped use styles, as the game loads them
        var center = session.SpawnCell;
        var chunk = ChunkMath.CellToChunk(center);
        for (int y = -1; y <= 1; y++)
            for (int z = -1; z <= 1; z++)
                for (int x = -1; x <= 1; x++)
                {
                    var c = chunk + new Int3(x, y, z);
                    if (session.World.ChunkInBounds(c)) session.Install(session.Produce(c));
                }

        // Flat arena: stone floor two cells thick, air above, no pre-existing objects.
        var floor = new Int3(center.X, center.Y - 1, center.Z);
        var stone = session.Content.BlockId("stone");
        var batch = session.World.BeginEdit();
        for (int dz = -ArenaRadius; dz <= ArenaRadius; dz++)
            for (int dx = -ArenaRadius; dx <= ArenaRadius; dx++)
            {
                for (int dy = -1; dy <= 0; dy++) batch.SetBlock(floor.X + dx, floor.Y + dy, floor.Z + dz, stone);
                for (int dy = 1; dy <= ArenaHeight; dy++)
                {
                    batch.SetBlock(floor.X + dx, floor.Y + dy, floor.Z + dz, 0);
                    batch.SetLiquid(floor.X + dx, floor.Y + dy, floor.Z + dz, 0);
                }
            }
        session.Commit(batch);
        var arena = new Aabb3i(floor - new Int3(ArenaRadius, 1, ArenaRadius), floor + new Int3(ArenaRadius + 1, ArenaHeight + 1, ArenaRadius + 1));
        foreach (var obj in session.World.TileObjects.InBox(arena).ToList()) session.World.TileObjects.Remove(obj);

        var player = PlayerState.CreateNew("tester", session.Content);
        world.AddPlayer(player, CellFeet(floor + Int3.Up));
        world.Town.DefenseEnabled = false; // the starter guide would shoot every test's creatures; TownFightBackTests turn it on
        return new TestGame(world, floor);
    }

    public static Vector3 CellFeet(Int3 cell) => new Vector3(cell.X + 0.5f, cell.Y, cell.Z + 0.5f) * Units.CellSize;

    public static Vector3 CellCenter(Int3 cell) => (new Vector3(cell.X, cell.Y, cell.Z) + new Vector3(0.5f)) * Units.CellSize;

    /// <summary>Puts <paramref name="count"/> of an item into the selected hotbar slot.</summary>
    public void Hold(string itemId, int count = 1, int slot = 0)
    {
        Player.State.Inventory.Backpack[slot] = new ItemStack(Content.Items.Get(itemId), count);
        Player.State.Inventory.Select(slot);
    }

    /// <summary>Aims the player's eye at a point (meters).</summary>
    public void AimAt(Vector3 point) => Player.Aim = Vector3.Normalize(point - Player.Eye);

    public Strata.Core.Gameplay.Interaction.Target? Target() =>
        Strata.Core.Gameplay.Interaction.Targeting.Cast(World.Session.World, Player.Eye, Player.Aim, 20f);

    /// <summary>Uses the held item at the current aim, waiting out cooldowns, up to <paramref name="maxUses"/> times while <paramref name="until"/> is false.</summary>
    public int UseUntil(Func<bool> until, int maxUses = 50)
    {
        int uses = 0;
        for (int tick = 0; tick < maxUses * 120 && uses < maxUses && !until(); tick++)
        {
            if (Player.UseCooldown == 0)
            {
                World.ItemUse.Use(Player, Target(), freshPress: true);
                uses++;
            }
            World.Tick();
        }
        return uses;
    }

    /// <summary>Like <see cref="UseUntil"/> with an explicit target source (e.g. digging under the feet).</summary>
    public int UseUntilTarget(Func<bool> until, Func<Strata.Core.Gameplay.Interaction.Target?> target, int maxUses = 50)
    {
        int uses = 0;
        for (int tick = 0; tick < maxUses * 120 && uses < maxUses && !until(); tick++)
        {
            if (Player.UseCooldown == 0)
            {
                World.ItemUse.Use(Player, target(), freshPress: true);
                uses++;
            }
            World.Tick();
        }
        return uses;
    }

    public void Ticks(int n)
    {
        for (int i = 0; i < n; i++) World.Tick();
    }

    public void Dispose() => World.Dispose();
}
