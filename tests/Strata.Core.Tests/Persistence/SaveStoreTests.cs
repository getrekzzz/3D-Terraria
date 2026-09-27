using System.Text.Json.Nodes;
using Strata.Core.Common;
using Strata.Core.Persistence;
using Strata.Core.Tests.Support;
using Strata.Core.World;

namespace Strata.Core.Tests.Persistence;

public class SaveStoreTests
{
    [Fact]
    public void MetaRoundTripsAndPreservesUnknownFields()
    {
        var dir = TestWorlds.TempDir();
        var store = new WorldSaveStore(dir);
        var meta = new WorldMeta { Name = "Test", Seed = 1234, Size = WorldSizePreset.Small, Flags = { "defeated_eye_of_cthulhu" } };
        meta.IdTables.Blocks.Add("stone");
        store.SaveMeta(meta);

        // Simulate a newer build adding a field.
        var path = Path.Combine(dir, WorldSaveStore.MetaFile);
        var node = JsonNode.Parse(File.ReadAllText(path))!.AsObject();
        node["futureFeature"] = new JsonObject { ["x"] = 5 };
        File.WriteAllText(path, node.ToJsonString());

        var loaded = store.LoadMeta();
        Assert.Equal(1234, loaded.Seed);
        Assert.Contains("defeated_eye_of_cthulhu", loaded.Flags);
        store.SaveMeta(loaded);
        Assert.Contains("futureFeature", File.ReadAllText(path));
    }

    [Fact]
    public void V0FixtureMigratesToCurrent()
    {
        var dir = TestWorlds.TempDir();
        File.WriteAllText(Path.Combine(dir, WorldSaveStore.MetaFile), """
            { "version": 0, "name": "Old", "seedText": "777", "blockIds": ["air", "stone"], "itemIds": ["torch"] }
            """);
        var meta = new WorldSaveStore(dir).LoadMeta();
        Assert.Equal(WorldMeta.CurrentVersion, meta.Version);
        Assert.Equal(777, meta.Seed);
        Assert.Equal(new[] { "air", "stone" }, meta.IdTables.Blocks);
        Assert.Equal(new[] { "torch" }, meta.IdTables.Items);
    }

    [Theory]
    [InlineData("umbral", EvilKind.Corruption, "corruption")]
    [InlineData("sanguine", EvilKind.Crimson, "crimson")]
    [InlineData("crimson", EvilKind.Crimson, "crimson")]
    [InlineData("corruption", EvilKind.Corruption, "corruption")]
    public void EvilKindReadsNamesFromBeforeTheRename(string saved, EvilKind expected, string written)
    {
        var root = TestWorlds.TempDir();
        var dir = Path.Combine(root, "old");
        Directory.CreateDirectory(dir);
        File.WriteAllText(Path.Combine(dir, WorldSaveStore.MetaFile), $$"""
            { "format": "strata-world", "version": 1, "name": "Old", "seed": 5, "size": "small", "difficulty": "classic", "evil": "{{saved}}" }
            """);
        var store = new WorldSaveStore(dir);
        Assert.Equal(expected, store.LoadMeta().Evil);
        var errors = new List<Exception>();
        var entry = Assert.Single(SaveCatalog.Worlds(root, (_, ex) => errors.Add(ex)));
        Assert.Empty(errors);
        Assert.Equal(expected, entry.Evil);
        store.SaveMeta(store.LoadMeta());
        Assert.Contains($"\"evil\": \"{written}\"", File.ReadAllText(Path.Combine(dir, WorldSaveStore.MetaFile)));
    }

    [Fact]
    public void NewerVersionsAreRejected()
    {
        var dir = TestWorlds.TempDir();
        File.WriteAllText(Path.Combine(dir, WorldSaveStore.MetaFile), """{ "version": 999, "format": "strata-world" }""");
        Assert.Throws<InvalidDataException>(() => new WorldSaveStore(dir).LoadMeta());
    }

    [Fact]
    public void ChunksRoundTripThroughRegionsAndCanBeDeleted()
    {
        var store = new WorldSaveStore(TestWorlds.TempDir());
        var a = new Int3(1, 2, 3);
        var b = new Int3(9, 2, 3); // different region
        store.SaveChunks(new Dictionary<Int3, byte[]?> { [a] = new byte[] { 1, 2, 3 }, [b] = new byte[] { 9 } });
        Assert.Equal(new byte[] { 1, 2, 3 }, store.LoadChunk(a));
        Assert.Equal(new byte[] { 9 }, store.LoadChunk(b));
        Assert.Null(store.LoadChunk(new Int3(0, 0, 0)));

        store.SaveChunks(new Dictionary<Int3, byte[]?> { [a] = null });
        Assert.Null(store.LoadChunk(a));
        Assert.NotNull(new WorldSaveStore(store.Directory).LoadChunk(b));
    }

    [Fact]
    public void AtomicWritesKeepABackup()
    {
        var path = Path.Combine(TestWorlds.TempDir(), "file.bin");
        AtomicFile.WriteAllBytes(path, new byte[] { 1 });
        AtomicFile.WriteAllBytes(path, new byte[] { 2 });
        Assert.Equal(new byte[] { 2 }, File.ReadAllBytes(path));
        Assert.Equal(new byte[] { 1 }, File.ReadAllBytes(path + ".bak"));
        File.WriteAllBytes(path, Array.Empty<byte>()); // corrupt primary
        Assert.Equal(new byte[] { 1 }, AtomicFile.ReadWithFallback(path, b => b.Length > 0));
    }
}
