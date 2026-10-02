using Strata.Core.Common;
using Strata.Core.World;
using Strata.Core.Tests.Support;
using Strata.Core.Voxels;
using Strata.Core.WorldGen;

namespace Strata.Core.Tests.WorldGen;

/// <summary>P5-22: Strange Plants grow on the surface of an ordinary tiny world.</summary>
public class StrangePlantGenTests
{
    [Fact]
    public void ATinyWorldGrowsStrangePlantsNearSpawn()
    {
        var gen = new WorldGenerator(TestPaths.Content, new WorldSettings { Seed = 1234, Size = WorldSizePreset.Tiny });
        int plants = 0, chunks = 0;
        for (int cz = 2; cz <= 14 && plants == 0; cz++)
            for (int cx = 2; cx <= 14; cx++)
            {
                var mid = ChunkMath.ChunkOrigin(new Int3(cx, 0, cz)) + new Int3(ChunkMath.Size / 2, 0, ChunkMath.Size / 2);
                int cy = (int)MathF.Floor(gen.Terrain.SurfaceHeight(mid.X, mid.Z) / ChunkMath.Size);
                for (int y = cy - 1; y <= cy + 1; y++)
                {
                    chunks++;
                    foreach (var o in gen.Generate(new Int3(cx, y, cz)).Objects)
                        if (o.Def.Tags.Contains("strange_plant")) plants++;
                }
            }
        Assert.True(plants >= 1, $"no Strange Plant in {chunks} surface chunks");
    }
}
