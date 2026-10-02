using Strata.Core.Persistence;
using Strata.Core.Tests.Support;

namespace Strata.Core.Tests.Persistence;

public class SaveFavoritesTests
{
    [Fact]
    public void StarsPersistByFileOrDirectoryName()
    {
        var dir = TestWorlds.TempDir();
        var path = Path.Combine(dir, "favorites.json");
        var favorites = SaveFavorites.Load(path);
        Assert.False(favorites.IsCharacter("/players/Hero.player.json"));
        Assert.True(favorites.ToggleCharacter("/players/Hero.player.json"));
        Assert.True(favorites.ToggleWorld("/worlds/Tour_World/"));

        var reloaded = SaveFavorites.Load(path);
        Assert.True(reloaded.IsCharacter("C:\\elsewhere\\Hero.player.json".Replace('\\', Path.DirectorySeparatorChar)));
        Assert.True(reloaded.IsWorld("/other/worlds/Tour_World"));
        Assert.False(reloaded.IsWorld("/worlds/Other"));

        Assert.False(reloaded.ToggleCharacter("/players/Hero.player.json"));
        reloaded.ForgetWorld("/worlds/Tour_World");
        var empty = SaveFavorites.Load(path);
        Assert.False(empty.IsCharacter("/players/Hero.player.json"));
        Assert.False(empty.IsWorld("/worlds/Tour_World"));
    }

    [Fact]
    public void UnreadableFileIsAnEmptyList()
    {
        var path = Path.Combine(TestWorlds.TempDir(), "favorites.json");
        File.WriteAllText(path, "{ not json");
        var favorites = SaveFavorites.Load(path);
        Assert.False(favorites.IsWorld("/worlds/a"));
        Assert.True(favorites.ToggleWorld("/worlds/a"));
        Assert.True(SaveFavorites.Load(path).IsWorld("/worlds/a"));
    }

    [Fact]
    public void StarredEntriesComeFirstInTheirOriginalOrder()
    {
        var names = new[] { "d", "a", "c", "b", "e" };
        var starred = new HashSet<string> { "c", "e" };
        Assert.Equal(new[] { "c", "e", "d", "a", "b" }, SaveFavorites.StarredFirst(names, starred.Contains));
    }
}
