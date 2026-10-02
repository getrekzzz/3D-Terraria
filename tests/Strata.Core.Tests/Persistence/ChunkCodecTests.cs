using Strata.Core.Common;
using Strata.Core.Content;
using Strata.Core.Content.Loading;
using Strata.Core.Gameplay.Items;
using Strata.Core.Persistence;
using Strata.Core.Tests.Support;
using Strata.Core.Voxels;
using Strata.Core.Voxels.TileObjects;

namespace Strata.Core.Tests.Persistence;

public class ChunkCodecTests
{
    private static ChunkData RandomChunk(ContentDatabase db, int seed)
    {
        var rng = new Random(seed);
        var palette = new[] { db.BlockId("air"), db.BlockId("stone"), db.BlockId("dirt"), db.BlockId("copper_ore"), db.BlockId("wood") };
        var blocks = new ushort[ChunkMath.Volume];
        var shapes = new byte[ChunkMath.Volume];
        var liquids = new byte[ChunkMath.Volume];
        for (int i = 0; i < blocks.Length; i++)
        {
            blocks[i] = palette[rng.Next(palette.Length)];
            if (rng.Next(20) == 0) shapes[i] = ShapeCode.Pack(CellShape.Slope, rng.Next(4));
            if (rng.Next(30) == 0) liquids[i] = LiquidCode.Pack(LiquidType.Water, rng.Next(1, 32));
        }
        return ChunkData.Compact(blocks, shapes, liquids, null);
    }

    [Fact]
    public void RoundTripPreservesAllLayersAndObjects()
    {
        var db = TestPaths.Content;
        var ids = new WorldIdTables();
        var data = RandomChunk(db, 1);
        var chest = new TileObjectInstance(db.TileObjects.Get("wooden_chest"), new Int3(3, 4, 5), 1, 0) { HitPoints = 2, Active = true, PendingLoot = "cabin_chest" };
        chest.Container![0] = new ItemStack(db.Items.Get("torch"), 17);
        chest.Container[5] = new ItemStack(db.Items.Get("copper_broadsword"), 1, db.Prefixes.Get("keen"));

        var blob = ChunkCodec.Encode(data, 3, new[] { chest }, db, ids);
        var decoded = ChunkCodec.Decode(blob, db, ids);

        for (int i = 0; i < ChunkMath.Volume; i++)
        {
            Assert.Equal(data.GetBlock(i), decoded.Data.GetBlock(i));
            Assert.Equal(data.Shapes![i], decoded.Data.Shapes![i]);
            Assert.Equal(data.Liquids![i], decoded.Data.Liquids![i]);
        }
        Assert.Equal(3, decoded.MutationLevel);
        var obj = Assert.Single(decoded.TileObjects);
        Assert.Equal(chest.Anchor, obj.Anchor);
        Assert.Equal(1, obj.Rotation);
        Assert.True(obj.Active);
        Assert.Equal("cabin_chest", obj.PendingLoot);
        Assert.Equal(17, obj.Container![0].Count);
        Assert.Equal("keen", obj.Container[5].Prefix!.Id);
    }

    [Fact]
    public void LockedStateRoundTrips()
    {
        var db = TestPaths.Content;
        var ids = new WorldIdTables();
        var data = RandomChunk(db, 2);
        var locked = new TileObjectInstance(db.TileObjects.Get("gold_chest"), new Int3(1, 2, 3), 0, 0) { Locked = true, PendingLoot = "dungeon_chest" };
        var open = new TileObjectInstance(db.TileObjects.Get("shadow_chest"), new Int3(8, 2, 3), 2, 0) { LootRolled = true, Active = true };
        var decoded = ChunkCodec.Decode(ChunkCodec.Encode(data, 0, new[] { locked, open }, db, ids), db, ids).TileObjects;
        var a = decoded.Single(o => o.Def.Id == "gold_chest");
        var b = decoded.Single(o => o.Def.Id == "shadow_chest");
        Assert.True(a.Locked);
        Assert.False(a.LootRolled);
        Assert.Equal("dungeon_chest", a.PendingLoot);
        Assert.False(b.Locked);
        Assert.True(b.LootRolled && b.Active);
    }

    [Fact]
    public void UniformChunksAreTiny()
    {
        var db = TestPaths.Content;
        var blob = ChunkCodec.Encode(new ChunkData(db.BlockId("stone")), 0, Array.Empty<TileObjectInstance>(), db, new WorldIdTables());
        Assert.True(blob.Length < 64, $"uniform chunk encoded to {blob.Length} bytes");
    }

    [Fact]
    public void UnknownIdsBecomePlaceholdersAndSurviveResave()
    {
        // Save with a content set that has an extra block, load with the shipped content.
        var modded = ContentLoader.LoadFromTexts(new[]
        {
            ("mod.json", """{ "blocks": [ { "id": "moon_rock", "textures": { "all": "stone" } } ] }"""),
        });
        var ids = new WorldIdTables();
        var blocks = new ushort[ChunkMath.Volume];
        blocks[0] = modded.BlockId("moon_rock");
        var blob = ChunkCodec.Encode(new ChunkData(blocks, null, null, null), 0, Array.Empty<TileObjectInstance>(), modded, ids);

        var db = ContentLoader.Load(new FileSystemDataSource(TestPaths.ProjectRoot));
        var decoded = ChunkCodec.Decode(blob, db, ids);
        var placeholder = db.Block(decoded.Data.GetBlock(0));
        Assert.Equal("moon_rock", placeholder.Id);
        Assert.True(placeholder.HasTag("placeholder"));

        var again = ChunkCodec.Decode(ChunkCodec.Encode(decoded.Data, 0, Array.Empty<TileObjectInstance>(), db, ids), db, ids);
        Assert.Equal("moon_rock", db.Block(again.Data.GetBlock(0)).Id);
    }

    [Fact]
    public void CorruptBlobsThrowInvalidData()
    {
        Assert.ThrowsAny<Exception>(() => ChunkCodec.Decode(new byte[] { 1, 2, 3, 4 }, TestPaths.Content, new WorldIdTables()));
    }
}
