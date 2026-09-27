using Strata.Core.Common;
using Strata.Core.Voxels;

namespace Strata.Core.Tests.Support;

public static class TestWorlds
{
    /// <summary>A world of the given chunk extent filled with uniform chunks of <paramref name="fill"/>.</summary>
    public static VoxelWorld Filled(string fill = "stone", int chunks = 3)
    {
        var db = TestPaths.Content;
        var world = new VoxelWorld(db, new Int3(chunks * 32, chunks * 32, chunks * 32), db.BlockId("stone"));
        ushort id = db.BlockId(fill);
        for (int y = 0; y < chunks; y++)
            for (int z = 0; z < chunks; z++)
                for (int x = 0; x < chunks; x++)
                    world.AddChunk(new Chunk(new Int3(x, y, z), new ChunkData(id)));
        return world;
    }

    public static string TempDir()
    {
        var dir = Path.Combine(Path.GetTempPath(), "strata-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        return dir;
    }
}
