using Strata.Core.Content.Defs;
using Strata.Core.Gameplay.Interaction;
using Strata.Core.Tests.Support;

namespace Strata.Core.Tests.Gameplay;

/// <summary>
/// The buff bar (P2-14): right-clicking a buff cancels it, never a debuff or a buff the player cannot shed; the minion
/// buff counts the summons of each kind and right-clicking it sends them home.
/// </summary>
public class BuffCancelTests
{
    [Fact]
    public void CancelRemovesBuffAndRecomputes()
    {
        using var g = TestGame.Create();
        var state = g.Player.State;
        int baseDefense = state.Defense;
        state.Buffs.Add(g.Content.Buffs.Get("ironskin"), 600);
        state.Recompute();
        Assert.Equal(baseDefense + 8, state.Defense);
        int changed = 0;
        state.Buffs.Changed += () => changed++;

        Assert.True(state.Buffs.TryCancel("ironskin"));
        state.Recompute();
        Assert.False(state.Buffs.Has("ironskin"));
        Assert.Equal(baseDefense, state.Defense);
        Assert.Equal(1, changed);
        Assert.False(state.Buffs.TryCancel("ironskin")); // no longer active
        Assert.Equal(1, changed);
    }

    [Fact]
    public void DebuffsAndPersistentBuffsCannotBeCancelled()
    {
        using var g = TestGame.Create();
        var buffs = g.Player.State.Buffs;
        buffs.Add(g.Content.Buffs.Get("potion_sickness"), 600);
        buffs.Add(g.Content.Buffs.Get("on_fire"), 600);
        buffs.Add(new BuffDef { Id = "test_bound", Persistent = true }, 600);

        Assert.False(buffs.TryCancel("potion_sickness"));
        Assert.False(buffs.TryCancel("on_fire"));
        Assert.False(buffs.TryCancel("test_bound"));
        Assert.Equal(3, buffs.Active.Count);
    }

    [Fact]
    public void MinionBuffCountsSummonsAndSendsThemHome()
    {
        using var g = TestGame.Create();
        g.World.Spawner.Enabled = false;
        g.Hold("slime_staff");
        g.Player.State.Vitals.RestoreMana(200);
        Assert.Equal(UseOutcome.Shot, g.World.ItemUse.Use(g.Player, null, true));
        var groups = new List<(ProjectileDef Def, int Count)>();

        g.World.Projectiles.CountMinions(g.Player, groups);
        var group = Assert.Single(groups);
        Assert.Equal("slime_minion", group.Def.Id);
        Assert.Equal(1, group.Count);

        Assert.Equal(1, g.World.Projectiles.Unsummon(g.Player, group.Def));
        g.World.Projectiles.CountMinions(g.Player, groups);
        Assert.Empty(groups);
        Assert.Empty(g.World.Projectiles.MinionsOf(g.Player));
    }
}
