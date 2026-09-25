using Strata.Core.Content.Loading;
using Strata.Core.Content.Reference;
using Strata.Core.Tests.Support;

namespace Strata.Core.Tests.Content;

public class ReferenceCoverageTests
{
    [Fact]
    public void EveryCoversLinkNamesAKnownConcept()
    {
        var catalog = ReferenceCatalog.Load(new FileSystemDataSource(TestPaths.ProjectRoot));
        var unknown = ReferenceCatalog.Covered(TestPaths.Content).Keys.Where(id => !catalog.Contains(id)).ToList();
        Assert.True(unknown.Count == 0, "unknown covers: " + string.Join(", ", unknown));
    }

    [Fact]
    public void ReferenceDatabasesAreBroad()
    {
        var catalog = ReferenceCatalog.Load(new FileSystemDataSource(TestPaths.ProjectRoot));
        Assert.True(catalog.ByCategory["items"].Count >= 400);
        Assert.True(catalog.ByCategory["enemies"].Count >= 120);
        Assert.True(catalog.ByCategory["bosses"].Count >= 19);
        Assert.True(catalog.ByCategory["npcs"].Count >= 25);
        var coverage = catalog.Coverage(TestPaths.Content);
        Assert.All(coverage, c => Assert.InRange(c.Percent, 0, 100));
    }
}
