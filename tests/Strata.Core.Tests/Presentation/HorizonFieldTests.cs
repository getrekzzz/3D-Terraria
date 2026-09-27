using System.Numerics;
using Strata.Core.Common;
using Strata.Core.Presentation;
using Strata.Core.Tests.Support;
using Strata.Core.World;
using Strata.Core.WorldGen;

namespace Strata.Core.Tests.Presentation;

public class HorizonFieldTests
{
    private static readonly WorldGenerator Generator = new(TestPaths.Content, new WorldSettings { Seed = 42, Size = WorldSizePreset.Tiny });
    private static readonly HorizonField Field = new(Generator);

    [Fact]
    public void SamplesFollowTheGeneratedSurfaceAndTheSea()
    {
        var size = Generator.Plan.Size;
        int land = 0, sea = 0;
        for (int i = 0; i < 400; i++)
        {
            float x = (i * 37 % 100) / 100f * size.X * Units.CellSize, z = (i * 61 % 100) / 100f * size.Z * Units.CellSize;
            var s = Field.Sample(x, z);
            float ground = Generator.Terrain.SurfaceHeight(x / Units.CellSize, z / Units.CellSize) * Units.CellSize;
            if (s.Water)
            {
                sea++;
                Assert.Equal(Field.SeaLevel, s.Height);
                Assert.True(ground < Field.SeaLevel);
            }
            else
            {
                land++;
                Assert.Equal(ground, s.Height, 3);
            }
            Assert.InRange(s.Color.X + s.Color.Y + s.Color.Z, 0.01f, 3f);
        }
        Assert.True(land > 0 && sea > 0, $"land {land}, sea {sea}");
        // Past the world's edge there is only sea.
        Assert.True(Field.Sample(-50f, 10f).Water);
        Assert.True(Field.Sample(size.X * Units.CellSize + 50f, 10f).Water);
    }

    [Fact]
    public void TheMeshIsARingOfWideningCellsAroundItsCentre()
    {
        var centre = new Vector2(Generator.Plan.Spawn.X, Generator.Plan.Spawn.Z) * Units.CellSize;
        var mesh = Field.Build(centre, 40f, 800f, 96);
        Assert.Equal(mesh.Positions.Count, mesh.Normals.Count);
        Assert.Equal(mesh.Positions.Count, mesh.Colors.Count);
        Assert.Equal(0, mesh.Positions.Count % 96);
        Assert.Equal(0, mesh.Indices.Count % 3);
        Assert.All(mesh.Indices, i => Assert.InRange(i, 0, mesh.Positions.Count - 1));
        float far = mesh.Positions.Max(p => Vector2.Distance(new Vector2(p.X, p.Z), centre));
        float near = mesh.Positions.Min(p => Vector2.Distance(new Vector2(p.X, p.Z), centre));
        Assert.InRange(far, 799f, 801f);
        Assert.InRange(near, 39f, 41f);
        Assert.All(mesh.Normals, n => Assert.True(n.Y > 0f));
        // Deterministic: the same centre gives the same mesh.
        var again = Field.Build(centre, 40f, 800f, 96);
        Assert.Equal(mesh.Positions, again.Positions);
    }
}
