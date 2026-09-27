using Strata.Core.Common;
using Strata.Core.Tests.Support;
using Strata.Core.World;

namespace Strata.Core.Tests.Session;

public class CaveFinderTests
{
    [Theory]
    [InlineData(WorldLayer.Underground)]
    [InlineData(WorldLayer.Cavern)]
    [InlineData(WorldLayer.Underworld)]
    public void FindsOpenCaveFloorsInEachLayer(WorldLayer layer)
    {
        using var g = TestGame.Create();
        var session = g.World.Session;
        var cave = session.FindCave(session.SpawnCell, layer);
        Assert.NotNull(cave);
        var c = cave!.Value;
        Assert.Equal(layer, session.Generator.Plan.Bands.LayerAt(c.Y));

        var coord = ChunkMath.CellToChunk(c);
        var data = session.World.GetChunk(coord)?.Capture() ?? session.Generator.Generate(coord).Data;
        var local = c - coord * ChunkMath.Size;
        bool Solid(int dy) => g.Content.Blocks[data.GetBlock(ChunkMath.Index(local.X, local.Y + dy, local.Z))].IsSolid;
        Assert.True(Solid(-1));
        for (int dy = 0; dy < 8; dy++) Assert.False(Solid(dy));
    }
}
