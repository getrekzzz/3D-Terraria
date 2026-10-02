using Strata.Core.Common;
using Strata.Core.Determinism;
using Strata.Core.WorldGen.Chunk;
using Strata.Core.WorldGen.Structures;

namespace Strata.Core.Tests.WorldGen;

public class BlueprintIndexTests
{
    private static Blueprint RandomBlueprint(ulong seed, int count)
    {
        var rng = new Pcg32(seed);
        var bp = new Blueprint();
        for (int i = 0; i < count; i++)
        {
            // Boxes from 1 to 70 cells around (and across) chunk borders, including negative coordinates.
            var min = new Int3(rng.Range(-40, 140), rng.Range(-40, 140), rng.Range(-40, 140));
            var box = new Aabb3i(min, min + new Int3(rng.Range(1, 70), rng.Range(1, 70), rng.Range(1, 70)));
            switch (rng.NextInt(5))
            {
                case 0: bp.Fill(box, "stone", noZone: rng.Chance(0.3f)); break;
                case 1: bp.Carve(box, 0, rng.Chance(0.3f)); break;
                case 2: bp.Replace(box, "dirt"); break;
                case 3: bp.Shell(box, "stone"); break;
                default: bp.Object("chest", box.Min, Int3.One); break;
            }
        }
        bp.Freeze();
        return bp;
    }

    private static Aabb3i Padded(Int3 chunk)
    {
        var origin = ChunkMath.ChunkOrigin(chunk) - Int3.One * ChunkGenContext.Pad;
        return new Aabb3i(origin, origin + Int3.One * ChunkGenContext.Size);
    }

    [Fact]
    public void OpsForMatchesBruteForceInOrder()
    {
        for (ulong seed = 1; seed <= 8; seed++)
        {
            var bp = RandomBlueprint(seed, 60);
            for (int cy = -3; cy <= 7; cy++)
                for (int cz = -3; cz <= 7; cz++)
                    for (int cx = -3; cx <= 7; cx++)
                    {
                        var chunk = new Int3(cx, cy, cz);
                        var box = Padded(chunk);
                        var expected = bp.Ops.Where(op => box.Intersects(op.Bounds)).ToArray();
                        Assert.Equal(expected, bp.OpsFor(chunk));
                    }
        }
    }

    [Fact]
    public void InZoneMatchesBruteForce()
    {
        var bp = RandomBlueprint(99, 80);
        var feature = new StructureFeature(bp) { Kind = "test" };
        var rng = new Pcg32(5);
        for (int i = 0; i < 5000; i++)
        {
            var cell = new Int3(rng.Range(-50, 220), rng.Range(-50, 220), rng.Range(-50, 220));
            bool expected = bp.Ops.Any(op => op is not ObjectOp && op is not CarveOp { NoZone: true } && op is not FillOp { NoZone: true }
                && op.Bounds.Contains(cell));
            Assert.Equal(expected, feature.InZone(cell));
        }
    }

    [Fact]
    public void NoZoneCarvesAreNotInTheZone()
    {
        var bp = new Blueprint();
        var room = new Aabb3i(new Int3(0, 0, 0), new Int3(10, 10, 10));
        bp.Room(room, "stone");
        bp.Carve(new Aabb3i(new Int3(3, 1, -8), new Int3(7, 6, 1)), noZone: true);
        bp.Fill(new Aabb3i(new Int3(3, 0, -12), new Int3(7, 1, -8)), "stone", noZone: true);
        var feature = new StructureFeature(bp) { Kind = "test" };
        Assert.True(feature.InZone(new Int3(5, 5, 5)));
        Assert.True(feature.InZone(new Int3(5, 3, 0))); // the cut through the wall is inside the room's fill
        Assert.False(feature.InZone(new Int3(5, 3, -4)));
        Assert.False(feature.InZone(new Int3(5, 0, -10)));
        Assert.Throws<InvalidOperationException>(() => bp.Carve(room));
    }
}
