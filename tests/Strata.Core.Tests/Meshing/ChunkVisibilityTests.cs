using Strata.Core.Common;
using Strata.Core.Meshing;
using Strata.Core.Tests.Support;

namespace Strata.Core.Tests.Meshing;

public class ChunkVisibilityTests
{
    [Fact]
    public void AWallOfSolidChunksHidesWhatIsBehindIt()
    {
        // Chunks at x = 2 are solid rock; everything else is open air.
        ushort Mask(Int3 c) => c.X == 2 ? (ushort)0 : ChunkVisibility.AllConnected;
        var visible = ChunkVisibility.Visible(Int3.Zero, Mask, 4, 4);
        Assert.Contains(new Int3(2, 0, 0), visible); // the wall's near face is seen
        Assert.DoesNotContain(new Int3(3, 0, 0), visible);
        Assert.DoesNotContain(new Int3(4, 1, -2), visible);
        Assert.Contains(new Int3(-4, 0, 0), visible);
    }

    [Fact]
    public void ATunnelOnlyLetsSightThroughAlongIt()
    {
        // A chunk crossed by an east-west tunnel: open between +X and -X only.
        ushort tunnel = (ushort)(1 << 0); // the PosX/NegX pair is the first bit
        Assert.True(ChunkVisibility.Connected(tunnel, Faces.PosX, Faces.NegX));
        Assert.False(ChunkVisibility.Connected(tunnel, Faces.PosX, Faces.PosY));
        ushort Mask(Int3 c) => c.X == 1 ? tunnel : c.X >= 2 && c.Y != 0 ? (ushort)0 : ChunkVisibility.AllConnected;
        var visible = ChunkVisibility.Visible(Int3.Zero, Mask, 4, 0);
        Assert.Contains(new Int3(3, 0, 0), visible);
    }

    [Fact]
    public void TheArenaChunkConnectsItsOpenFaces()
    {
        using var g = TestGame.Create();
        var session = g.World.Session;
        var tables = new RenderTables(session.Content, _ => 0);
        var chunk = ChunkMath.CellToChunk(g.Floor + Int3.Up);
        var nb = Neighborhood.Capture(session.World, chunk)!;
        ushort mask = ChunkVisibility.Compute(nb, tables);
        Assert.NotEqual(0, mask); // the air above the arena floor reaches several faces
    }
}
