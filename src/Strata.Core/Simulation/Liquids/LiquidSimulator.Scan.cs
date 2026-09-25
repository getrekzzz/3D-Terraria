using Strata.Core.Common;
using Strata.Core.Voxels;

namespace Strata.Core.Simulation.Liquids;

public sealed partial class LiquidSimulator
{
    /// <summary>
    /// A chunk joined the world. Generated liquid is static until something disturbs it (an edit wakes it), so a
    /// fresh chunk wakes nothing; a chunk from the save may have been saved mid-flow, so its liquid cells that can
    /// still move are woken, and so is the facing layer of played-in neighbours that may now flow into it.
    /// Settled liquid stays asleep, so loading an ocean costs one pass over its cells.
    /// </summary>
    private void OnChunkInstalled(Chunk chunk, bool fromSave)
    {
        var data = chunk.Capture();
        if (fromSave && data.Liquids != null) ScanChunk(chunk.Coord, data, Int3.Zero, new Int3(ChunkMath.Size, ChunkMath.Size, ChunkMath.Size));
        const int last = ChunkMath.Size - 1;
        for (int f = 0; f < Faces.Count; f++)
        {
            var o = Faces.Offsets[f];
            var neighbour = World.GetChunk(chunk.Coord + o);
            if (neighbour == null || !(neighbour.FromSave || neighbour.Modified)) continue;
            var nd = neighbour.Capture();
            if (nd.Liquids == null) continue;
            // The neighbour's layer touching this chunk.
            var min = new Int3(o.X > 0 ? 0 : o.X < 0 ? last : 0, o.Y > 0 ? 0 : o.Y < 0 ? last : 0, o.Z > 0 ? 0 : o.Z < 0 ? last : 0);
            var max = new Int3(o.X != 0 ? min.X + 1 : ChunkMath.Size, o.Y != 0 ? min.Y + 1 : ChunkMath.Size, o.Z != 0 ? min.Z + 1 : ChunkMath.Size);
            ScanChunk(neighbour.Coord, nd, min, max);
        }
    }

    /// <summary>Wakes liquid cells in [min, max) (chunk-local) that a flow step would change.</summary>
    private void ScanChunk(Int3 coord, ChunkData data, Int3 min, Int3 max)
    {
        var liquids = data.Liquids!;
        var cells = new ChunkCells(coord, data, World);
        var origin = coord * ChunkMath.Size;
        for (int y = min.Y; y < max.Y; y++)
            for (int z = min.Z; z < max.Z; z++)
                for (int x = min.X; x < max.X; x++)
                {
                    byte l = liquids[ChunkMath.Index(x, y, z)];
                    if (l == 0) continue;
                    var cell = origin + new Int3(x, y, z);
                    if (_content.LiquidByCode[l >> 5] != null && _rules.CouldFlow(ref cells, cell, l)) _active[l >> 5].Add(cell);
                }
    }
}
