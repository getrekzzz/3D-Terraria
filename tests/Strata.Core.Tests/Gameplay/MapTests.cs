using Strata.Core.Common;
using Strata.Core.Gameplay.Map;
using Strata.Core.Tests.Support;
using Strata.Core.Voxels;

namespace Strata.Core.Tests.Gameplay;

/// <summary>The discoverable map: revealed around the player as they explore, remembered per player per world.</summary>
public class MapTests
{
    private static (int Px, int Pz) PixelOf(Int3 cell) => (cell.X / ExploredMap.CellsPerPixel, cell.Z / ExploredMap.CellsPerPixel);

    [Fact]
    public void OutdoorsTheSurfaceAroundThePlayerIsRevealed()
    {
        using var g = TestGame.Create();
        g.World.Spawner.Enabled = false;
        var map = g.World.Maps.For(g.Player);
        Assert.Equal(0, map.CountExplored());
        g.World.Maps.Reveal(g.Player, force: true);
        var (px, pz) = PixelOf(g.Floor);
        ushort here = map.Surface(px, pz);
        Assert.Equal(g.Content.BlockId("stone"), MapValue.BlockId(here)); // the arena floor
        Assert.True(map.CountExplored() > 500);
        Assert.Equal(MapValue.Unexplored, map.Surface(px + MapSystem.SurfaceRadius, pz + MapSystem.SurfaceRadius)); // beyond the reveal radius
        Assert.Equal(MapValue.Unexplored, map.Cave(px, pz)); // nothing seen underground yet
    }

    [Fact]
    public void PlayingRevealsTheMapWithoutBeingAsked()
    {
        using var g = TestGame.Create();
        g.World.Spawner.Enabled = false;
        var map = g.World.Maps.For(g.Player);
        g.Ticks(MapSystem.RevealInterval * 2);
        int first = map.CountExplored();
        Assert.True(first > 500, $"only {first} pixels revealed by playing");
        g.Player.Position += new System.Numerics.Vector3(6f, 0, 0); // walk on: more comes into view
        g.Ticks(MapSystem.RevealInterval * 2);
        Assert.True(map.CountExplored() > first);
    }

    [Fact]
    public void UndergroundTheCaveFloorAtThePlayersDepthIsRevealed()
    {
        using var g = TestGame.Create();
        g.World.Spawner.Enabled = false;
        // Roof the arena over: the player is now "underground" in a sealed room.
        var batch = g.World.Session.World.BeginEdit();
        var stone = g.Content.BlockId("stone");
        for (int dz = -TestGame.ArenaRadius; dz <= TestGame.ArenaRadius; dz++)
            for (int dx = -TestGame.ArenaRadius; dx <= TestGame.ArenaRadius; dx++)
                batch.SetBlock(g.Floor.X + dx, g.Floor.Y + 7, g.Floor.Z + dz, stone);
        g.World.Session.Commit(batch);
        g.Ticks(2);
        var map = g.World.Maps.For(g.Player);
        g.World.Maps.Reveal(g.Player, force: true);
        var (px, pz) = PixelOf(g.Floor);
        ushort floor = map.Cave(px, pz);
        Assert.False(MapValue.IsWall(floor));
        Assert.Equal(stone, MapValue.BlockId(floor));
        Assert.Equal(MapValue.Unexplored, map.Surface(px, pz)); // the roof hid the sky: no surface seen
    }

    [Fact]
    public void MapsRoundTripByBlockName()
    {
        var map = new ExploredMap(512, 512);
        map.SetSurface(10, 20, MapValue.Block(7), 300);
        map.SetCave(10, 20, MapValue.Wall(9), 250);
        map.SetSurface(200, 3, MapValue.Liquid(LiquidType.Lava), 120);
        var names = new Dictionary<int, string> { [7] = "grass", [9] = "stone" };
        var bytes = map.Save(id => names[id]);
        // A later build numbers blocks differently and no longer has "stone".
        var loaded = ExploredMap.Load(bytes, 512, 512, name => name == "grass" ? 3 : null);
        Assert.Equal(MapValue.Block(3), loaded.Surface(10, 20));
        Assert.Equal(MapValue.Unexplored, loaded.Cave(10, 20));
        Assert.Equal(LiquidType.Lava, MapValue.LiquidOf(loaded.Surface(200, 3)));
        Assert.Throws<InvalidDataException>(() => ExploredMap.Load(bytes, 1024, 1024, _ => 1));
    }

    [Fact]
    public void MapsAreSavedPerPlayerInTheWorldFolder()
    {
        var dir = TestWorlds.TempDir();
        using (var g = TestGame.Create(dir))
        {
            g.World.Spawner.Enabled = false;
            g.World.Maps.Reveal(g.Player, force: true);
            g.World.SaveAll();
        }
        Assert.True(File.Exists(Path.Combine(dir, MapSystem.MapDirectory, "tester.map")));
        using var again = TestGame.Create(dir);
        Assert.True(again.World.Maps.For(again.Player).CountExplored() > 500);
    }
}
