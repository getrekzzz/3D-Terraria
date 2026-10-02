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
    public void ChestPrefixesFromOldSavesMapOrDropButKeepTheItem()
    {
        // Chests written before P3-31 hold invented prefix ids: they resolve through the aliases, and one that no
        // longer fits its item (Mythic on a spear, the old magic Arcane on a staff) is dropped, never the item.
        var db = TestPaths.Content;
        var ids = new WorldIdTables();
        static Strata.Core.Content.Defs.PrefixDef Old(string id) => new() { Id = id, Name = id };
        var chest = new TileObjectInstance(db.TileObjects.Get("wooden_chest"), new Int3(3, 4, 5), 0, 0) { Active = true };
        chest.Container![0] = new ItemStack(db.Items.Get("copper_broadsword"), 1, Old("towering"));
        chest.Container[1] = new ItemStack(db.Items.Get("spear"), 1, Old("mythic"));
        chest.Container[2] = new ItemStack(db.Items.Get("amethyst_staff"), 1, Old("arcane"));
        var blob = ChunkCodec.Encode(RandomChunk(db, 2), 0, new[] { chest }, db, ids);
        var c = Assert.Single(ChunkCodec.Decode(blob, db, ids).TileObjects).Container!;
        Assert.Equal("massive", c[0].Prefix!.Id);
        Assert.Equal("spear", c[1].Item!.Id);
        Assert.Null(c[1].Prefix);
        Assert.Equal("amethyst_staff", c[2].Item!.Id);
        Assert.Null(c[2].Prefix);
    }

    [Fact]
    public void ChestNameRoundTrips()
    {
        var db = TestPaths.Content;
        var ids = new WorldIdTables();
        var named = new TileObjectInstance(db.TileObjects.Get("wooden_chest"), new Int3(3, 4, 5), 0, 0) { LootRolled = true, CustomName = "Ore stash" };
        named.Container![2] = new ItemStack(db.Items.Get("torch"), 9);
        var plain = new TileObjectInstance(db.TileObjects.Get("wooden_chest"), new Int3(10, 4, 5), 0, 0);
        var decoded = ChunkCodec.Decode(ChunkCodec.Encode(new ChunkData(db.BlockId("stone")), 0, new[] { named, plain }, db, ids), db, ids);
        Assert.Equal(2, decoded.TileObjects.Count);
        var a = decoded.TileObjects[0];
        Assert.Equal("Ore stash", a.CustomName);
        Assert.True(a.LootRolled);
        Assert.False(a.Active);
        Assert.Equal(9, a.Container![2].Count);
        Assert.Null(decoded.TileObjects[1].CustomName); // the second record still lines up after the first one's meta
        Assert.Equal(new Int3(10, 4, 5), decoded.TileObjects[1].Anchor);
    }

    [Fact]
    public void ChunkWithoutNameStillReads()
    {
        // A chunk written before chest names: the object record ends with PendingLoot and the slots, state bits 1 and 2 only.
        var db = TestPaths.Content;
        var ids = new WorldIdTables();
        var raw = Inflate(ChunkCodec.Encode(new ChunkData(db.BlockId("stone")), 0, Array.Empty<TileObjectInstance>(), db, ids));
        using var ms = new MemoryStream();
        ms.Write(raw, 0, raw.Length - 1); // drop the end tag, append an old-style object section
        using (var w = new BinaryWriter(ms, System.Text.Encoding.UTF8, leaveOpen: true))
        {
            w.WriteSection(5, s =>
            {
                s.WriteVar(1);
                s.WriteVar(ids.TileObjects.Intern("wooden_chest"));
                s.WriteZig(3);
                s.WriteZig(-4);
                s.WriteZig(5);
                s.Write((byte)0);
                s.Write((byte)0);
                s.WriteVar(3);
                s.Write((byte)3);
                s.Write("cabin_chest");
                s.WriteVar(40);
                s.WriteVar(ids.Items.Intern("torch") + 1);
                s.WriteVar(17);
                s.WriteVar(0);
                for (int i = 1; i < 40; i++) s.WriteVar(0);
            });
            w.Write((byte)0);
        }
        var obj = Assert.Single(ChunkCodec.Decode(Deflate(ms.ToArray()), db, ids).TileObjects);
        Assert.Null(obj.CustomName);
        Assert.True(obj.Active);
        Assert.True(obj.LootRolled);
        Assert.Equal("cabin_chest", obj.PendingLoot);
        Assert.Equal(new Int3(3, -4, 5), obj.Anchor);
        Assert.Equal(17, obj.Container![0].Count);
    }

    private static byte[] Inflate(byte[] blob)
    {
        using var z = new System.IO.Compression.ZLibStream(new MemoryStream(blob), System.IO.Compression.CompressionMode.Decompress);
        using var o = new MemoryStream();
        z.CopyTo(o);
        return o.ToArray();
    }

    private static byte[] Deflate(byte[] raw)
    {
        using var o = new MemoryStream();
        using (var z = new System.IO.Compression.ZLibStream(o, System.IO.Compression.CompressionLevel.Fastest, leaveOpen: true)) z.Write(raw);
        return o.ToArray();
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
