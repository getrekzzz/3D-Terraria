using Strata.Core.Content;
using Strata.Core.Content.Loading;

namespace Strata.Core.Tests.Support;

/// <summary>Locates the repository and loads the real game content once per test run.</summary>
public static class TestPaths
{
    public static readonly string ProjectRoot = FileSystemDataSource.FindProjectRoot(AppContext.BaseDirectory);

    private static readonly Lazy<ContentDatabase> LazyContent = new(() => ContentLoader.Load(new FileSystemDataSource(ProjectRoot)));

    private static readonly Lazy<Strata.Core.Gameplay.Items.UseTiming> LazyTiming =
        new(() => Strata.Core.Gameplay.Items.UseTiming.Load(new FileSystemDataSource(ProjectRoot)));

    /// <summary>The shipped game content (validated).</summary>
    public static ContentDatabase Content => LazyContent.Value;

    /// <summary>The shipped use styles and clip timing (data/config/use_styles.json, validated).</summary>
    public static Strata.Core.Gameplay.Items.UseTiming Timing => LazyTiming.Value;
}
