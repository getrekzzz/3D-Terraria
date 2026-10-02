using Strata.Core.Content.Specs;
using Strata.Core.Tests.Support;

namespace Strata.Core.Tests.Gameplay;

/// <summary>Per-hook looks (P3-17'): gem hooks glow, the Ivy Whip is a vine, the rest are chains; bad styles are reported.</summary>
public class HookStyleTests
{
    private static GrappleParams Hook(string id) => new Strata.Core.Gameplay.Items.ItemUseTable(TestPaths.Content)[TestPaths.Content.Items.Get(id)].Grapple!;

    [Fact]
    public void ShippedHooksHaveTheirChains()
    {
        Assert.Equal("links", Hook("grappling_hook").Chain);
        Assert.Equal("gem", Hook("ruby_hook").Chain);
        Assert.Equal("vine", Hook("ivy_whip").Chain);
        Assert.Null(Hook("grappling_hook").Head); // the hook item's own model
    }

    [Fact]
    public void AnUnknownChainIsReported()
    {
        Assert.NotNull(new GrappleParams { Chain = "rope" }.Validate());
        Assert.Null(new GrappleParams { Chain = "gem" }.Validate());
    }
}
