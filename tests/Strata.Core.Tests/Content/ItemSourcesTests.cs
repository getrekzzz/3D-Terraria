using Strata.Core.Content;
using Strata.Core.Content.Defs;
using Strata.Core.Content.Index;
using Strata.Core.Content.Loading;
using Strata.Core.Tests.Support;

namespace Strata.Core.Tests.Content;

public class ItemSourcesTests
{
    /// <summary>
    /// Shipped items that no source gives yet, each with the work that makes it obtainable. Remove an id once that lands:
    /// the list must match exactly, so a new unobtainable item fails here instead of hiding behind a validate warning.
    /// </summary>
    private static readonly string[] Pending =
    {
        "rally", // sold by the Skeleton Merchant (P5-19)
    };

    private static readonly Lazy<ItemSourceIndex> Shipped = new(() => ItemSourceIndex.Build(TestPaths.Content));

    private static bool Has(string item, ItemSourceKind kind, string? owner = null) =>
        Shipped.Value.For(item).Any(s => s.Kind == kind && (owner == null || s.Owner?.Id == owner));

    [Fact]
    public void ShippedContentHasNoUnexpectedObtainabilityWarnings()
    {
        Assert.Equal(Pending.Order(), Shipped.Value.Unobtainable.Select(i => i.Id).Order());
        var warned = TestPaths.Content.Report.Warnings.Where(w => w.Message.Contains("source") || w.Message.Contains("no resident sells it"))
            .Select(w => w.Subject).Order();
        Assert.Equal(Pending.Select(id => $"ItemDef:{id}").Order(), warned);
    }

    [Fact]
    public void EveryShippedLootTableIsRolledBySomething()
    {
        // World generation fills chests by table id, so its tables carry a name ("Gold Chests") to count as containers.
        var unowned = TestPaths.Content.LootTables.All.Where(t => !Shipped.Value.IsOwned(t.Id)).Select(t => t.Id).ToList();
        Assert.Empty(unowned);
        Assert.DoesNotContain(TestPaths.Content.Report.Warnings, w => w.Message.Contains("not rolled"));
    }

    [Fact]
    public void ParityWithOldValidator()
    {
        var db = TestPaths.Content;
        var old = OldSources(db);
        var index = Shipped.Value;
        // Tags are sources of their own now; the old check exempted them instead.
        var fromIndex = db.Items.All.Where(i => index.For(i.Id).Any(s => s.Kind != ItemSourceKind.Tag)).Select(i => i.Id).ToHashSet();
        var fromOld = db.Items.All.Where(i => old.Contains(i.Id)).Select(i => i.Id).ToHashSet();
        Assert.Empty(fromOld.Except(fromIndex));
        // Angler rewards are new: the fishing rods they give were only excused by a 'shop' tag before. Enemy banners
        // (dropped every so many kills) are new too.
        var gained = fromIndex.Except(fromOld).ToList();
        Assert.NotEmpty(gained);
        Assert.All(gained, id => Assert.Contains(index.For(id), s => s.Kind == ItemSourceKind.AnglerReward
            || (s.Kind == ItemSourceKind.Drop && db.TileObjects.TryGet(id, out var t) && t.Banner != null)));
    }

    [Fact]
    public void ResolvesOwnersOfEachKind()
    {
        Assert.True(Has("gel", ItemSourceKind.Drop, "green_slime"));
        Assert.True(Has("gel", ItemSourceKind.BossDrop, "king_slime"));
        Assert.True(Has("torch", ItemSourceKind.Recipe));
        Assert.True(Has("torch", ItemSourceKind.Shop, "merchant"));
        Assert.True(Has("water_bucket", ItemSourceKind.Bucket, "water"));
        Assert.True(Has("bunny", ItemSourceKind.Catch, "bunny"));
        Assert.True(Has("stone", ItemSourceKind.Block, "stone"));
        Assert.True(Has("wooden_crate", ItemSourceKind.Fishing));
        Assert.True(Has("tackle_box", ItemSourceKind.AnglerReward, "angler"));
        Assert.True(Has("copper_bar", ItemSourceKind.Opened, "wooden_crate"));
        Assert.True(Has("daybloom_seeds", ItemSourceKind.Object, "daybloom_plant"));

        var chest = Shipped.Value.For("gold_bar").First(s => s.Kind == ItemSourceKind.Container && s.Label == "Gold Chests");
        Assert.Equal((5, 12), (chest.Min, chest.Max));
        // The pot table belongs to the pot object, not to a named container.
        Assert.DoesNotContain(Shipped.Value.For("torch"), s => s.Kind == ItemSourceKind.Container && s.Label == "Pots");
    }

    [Fact]
    public void NestedTablesInheritOwnerAndConditions()
    {
        var db = ContentLoader.LoadFromTexts(new[] { ("test.json", """
        { "items": [ { "id": "gem" }, { "id": "ore" }, { "id": "wand", "tags": ["shop"] }, { "id": "kit", "tags": ["starter"] } ],
          "enemies": [ { "id": "golem", "loot": "outer" } ],
          "lootTables": [
            { "id": "outer", "entries": [ { "table": "inner", "conditions": [ { "type": "moon", "phases": [0] } ] } ] },
            { "id": "inner", "entries": [ { "item": "gem", "min": 2, "max": 3, "conditions": [ { "type": "difficulty", "min": 1 } ] } ] },
            { "id": "vault", "name": "Vaults", "entries": [ { "item": "ore" } ] },
            { "id": "forgotten", "entries": [ { "item": "ore" } ] }
          ] }
        """) });
        var index = ItemSourceIndex.Build(db);

        var gem = Assert.Single(index.For("gem"));
        Assert.Equal(ItemSourceKind.Drop, gem.Kind);
        Assert.Equal("golem", gem.Owner!.Id);
        Assert.Equal((2, 3), (gem.Min, gem.Max));
        Assert.Equal(new[] { "moon", "difficulty" }, gem.Conditions.Select(c => c.Type));

        // A named table nothing rolls is a world-gen container; an unnamed one gives nothing.
        var ore = Assert.Single(index.For("ore"));
        Assert.Equal((ItemSourceKind.Container, "Vaults"), (ore.Kind, ore.Label));
        Assert.True(index.IsOwned("inner"));
        Assert.False(index.IsOwned("forgotten"));
        Assert.Contains(db.Report.Warnings, w => w.Subject == "LootTableDef:forgotten" && w.Message.Contains("not rolled"));

        // A 'shop' tag is no excuse: something has to sell it. Starter items are their own source.
        Assert.Equal(new[] { "wand" }, index.Unobtainable.Select(i => i.Id));
        Assert.Contains(db.Report.Warnings, w => w.Subject == "ItemDef:wand" && w.Message.Contains("no resident sells it"));
        Assert.Equal(ItemSourceKind.Tag, Assert.Single(index.For("kit")).Kind);
    }

    /// <summary>The obtainability enumeration as it was in ContentValidator.World.cs before the index existed.</summary>
    private static HashSet<string> OldSources(ContentDatabase db)
    {
        var sources = new HashSet<string>(StringComparer.Ordinal);
        foreach (var r in db.Recipes.All) sources.Add(r.Output);
        foreach (var t in db.LootTables.All)
            foreach (var e in t.Entries)
                if (e.Item != null) sources.Add(e.Item);
        foreach (var d in db.BlockDrops)
            if (d != null) sources.Add(d.Id);
        foreach (var t in db.TileObjects.All)
            if (t.PlacingItemId != null && (t.Natural || t.HasTag("world"))) sources.Add(t.PlacingItemId);
        foreach (var e in db.Enemies.All)
            if (e.CatchItem != null) sources.Add(e.CatchItem);
        foreach (var t in db.TownNpcs.All)
            foreach (var e in t.Shop) sources.Add(e.Item);
        if (db.Items.All.Any(i => sources.Contains(i.Id) && i.Behaviors.Any(b => b.Type == Core.Content.Specs.ItemBehaviorSpecs.Bucket && !b.Raw.TryGetProperty("liquid", out _))))
            foreach (var l in db.Liquids.All)
                if (l.Bucket != null) sources.Add(l.Bucket);
        return sources;
    }
}
