using Strata.Core.Content.Defs;
using Strata.Core.Content.Loading;
using Strata.Core.Gameplay.Items;
using Strata.Core.Presentation;
using Strata.Core.Tests.Support;

namespace Strata.Core.Tests.Presentation;

/// <summary>P3-27: the sound effect voice pool's stealing policy and the per-item sound set.</summary>
public class SfxVoicePoolTests
{
    [Fact]
    public void FreeVoicesAreUsedFirstAndEndByTime()
    {
        var pool = new SfxVoicePool(4, 3);
        Assert.Equal(0, pool.Acquire(1, 0, 0.5, 1f, out bool stole));
        Assert.False(stole);
        Assert.Equal(1, pool.Acquire(2, 0.1, 0.5, 1f, out _));
        Assert.Equal(2, pool.Busy(0.2));
        Assert.Equal(1, pool.Busy(0.55)); // the first ended at 0.5
        Assert.Equal(0, pool.Acquire(3, 0.55, 0.5, 1f, out stole)); // its voice is free again
        Assert.False(stole);
        Assert.Equal(0, pool.Stolen);
    }

    [Fact]
    public void OneIdHoldsAtMostItsCapAndStealsItsOwnOldest()
    {
        var pool = new SfxVoicePool(8, 3);
        pool.Acquire(9, 0, 5, 1f, out _); // another sound, older than all
        int first = pool.Acquire(7, 0.1, 5, 1f, out _);
        pool.Acquire(7, 0.2, 5, 1f, out _);
        pool.Acquire(7, 0.3, 5, 1f, out _);
        int fourth = pool.Acquire(7, 0.4, 5, 1f, out bool stole);
        Assert.True(stole);
        Assert.Equal(first, fourth); // not the older other sound
        Assert.Equal(4, pool.Busy(0.5));
        Assert.Equal(1, pool.Stolen);
    }

    [Fact]
    public void AFullPoolStealsTheOldestThenTheQuietestAndNeverExceedsItsSize()
    {
        var pool = new SfxVoicePool(3, 3);
        pool.Acquire(1, 0, 10, 1f, out _);
        int quiet = pool.Acquire(2, 0, 10, 0.2f, out _);
        pool.Acquire(3, 0.1, 10, 1f, out _);
        Assert.Equal(quiet, pool.Acquire(4, 0.2, 10, 1f, out bool stole)); // as old as voice 0, but quieter
        Assert.True(stole);
        for (int i = 0; i < 50; i++) pool.Acquire(100 + i, 0.3 + i * 0.01, 10, 1f, out _);
        Assert.Equal(3, pool.PeakBusy);
        Assert.Equal(51, pool.Stolen);
    }

    [Fact]
    public void ItemSoundSetWinsThenTheStyleThenNothing()
    {
        var uses = new ItemUseTable(TestPaths.Content);
        var styles = UseStyleConfig.Load(new FileSystemDataSource(TestPaths.ProjectRoot));
        Func<string, bool> all = _ => true;
        Assert.Equal("shotgun", ItemSounds.Resolve(uses[TestPaths.Content.Items.Get("boomstick")], styles, all));
        Assert.Equal("gun", ItemSounds.Resolve(uses[TestPaths.Content.Items.Get("minishark")], styles, all));
        Assert.Equal("eat", ItemSounds.Resolve(uses[FirstWithStyle(uses, UseStyle.Eat)], styles, all));
        // A sound set without a file falls back to the style's sound.
        Assert.Equal("gun_heavy", ItemSounds.Resolve(uses[TestPaths.Content.Items.Get("boomstick")], styles, s => s != "shotgun"));
        Assert.Null(ItemSounds.Resolve(uses[TestPaths.Content.Items.Get("minishark")], styles, _ => false));
    }

    private static ItemDef FirstWithStyle(ItemUseTable uses, UseStyle style) =>
        TestPaths.Content.Items.All.First(i => uses[i].Style == style && i.SoundSet == null);
}
