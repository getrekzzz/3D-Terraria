using System.Linq;
using Strata.Core.Presentation;
using Strata.Core.Tests.Support;

namespace Strata.Core.Tests.Presentation;

public class BossIntroModelTests
{
    private static BossIntroModel Awoken(float framing = 2.5f, float letterbox = 2f)
    {
        var m = new BossIntroModel();
        m.Awaken("skeletron", "Skeletron", "Curse of the Old Man", "#e0d6bc", framing, letterbox);
        return m;
    }

    private static void Run(BossIntroModel m, float seconds, float step = 1f / 60f)
    {
        for (float t = 0; t < seconds - 1e-4f; t += step) m.Advance(step);
    }

    [Fact]
    public void AwokenCardFadesInHoldsAndFadesOut()
    {
        var m = Awoken();
        Assert.True(m.Active);
        Assert.Equal(BossCardKind.Awoken, m.Kind);
        Assert.Equal("Curse of the Old Man", m.Subtitle);
        Assert.Equal(1, m.Shown);
        Assert.Equal(0f, m.Alpha);
        Assert.Equal(BossIntroModel.PopScale, m.TitleScale, 4);
        m.Advance(BossIntroModel.FadeIn);
        Assert.Equal(1f, m.Alpha, 4);
        Assert.Equal(1f, m.TitleScale, 4);
        m.Advance(BossIntroModel.Hold);
        Assert.Equal(1f, m.Alpha, 4);
        m.Advance(BossIntroModel.FadeOut / 2);
        Assert.InRange(m.Alpha, 0.3f, 0.7f);
        m.Advance(BossIntroModel.FadeOut / 2 + 0.01f);
        Assert.False(m.Active);
        Assert.Equal(0f, m.Alpha);
        Assert.Equal(BossCardKind.None, m.Kind);
    }

    [Fact]
    public void FramingNeverOutlastsItsWindowAndNeverSlowsTime()
    {
        var m = Awoken(framing: 3f);
        float peak = 0f;
        for (int i = 0; i < 300; i++)
        {
            m.Advance(1f / 60f);
            peak = System.Math.Max(peak, m.Framing);
            Assert.Equal(1f, m.TimeScale);
            if (m.Time >= BossIntroModel.FramingSeconds) Assert.Equal(0f, m.Framing);
        }
        Assert.Equal(3f, peak, 2);
        var far = Awoken(framing: 50f);
        far.Advance(BossIntroModel.FramingIn);
        Assert.Equal(BossIntroModel.MaxFramingDistance, far.Framing, 3);
    }

    [Fact]
    public void LetterboxHoldsForItsSecondsThenRetracts()
    {
        var m = Awoken(letterbox: 1.6f);
        m.Advance(BossIntroModel.BarsIn);
        Assert.Equal(1f, m.Bars, 4);
        Run(m, 1.6f - BossIntroModel.BarsIn);
        Assert.Equal(1f, m.Bars, 2);
        Run(m, BossIntroModel.BarsOut + 0.02f);
        Assert.Equal(0f, m.Bars);
        Assert.True(m.Active); // the card outlasts the bars
        var none = Awoken(letterbox: 0f);
        none.Advance(0.5f);
        Assert.Equal(0f, none.Bars);
    }

    [Fact]
    public void SkipFadesEverythingOutQuickly()
    {
        var m = Awoken();
        m.Advance(1f);
        Assert.True(m.Skip());
        Assert.False(m.Skip()); // already going
        Assert.Equal(1, m.Skips);
        float alpha = m.Alpha, framing = m.Framing;
        Assert.Equal(1f, alpha, 4);
        Assert.True(framing > 0f);
        m.Advance(BossIntroModel.SkipFade / 2);
        Assert.InRange(m.Alpha, 0.4f, 0.6f);
        Assert.InRange(m.Framing, framing * 0.4f, framing * 0.6f);
        m.Advance(BossIntroModel.SkipFade / 2 + 0.001f);
        Assert.False(m.Active);
        Assert.False(m.Skip());
    }

    [Fact]
    public void EarlySkipWaitsUntilTheCardHasShown()
    {
        var m = Awoken();
        m.Advance(0.1f);
        Assert.True(m.Skip());
        Assert.False(m.Skipped);
        m.Advance(BossIntroModel.MinShown - 0.1f - 0.05f);
        Assert.False(m.Skipped);
        Assert.True(m.Alpha > 0.9f);
        m.Advance(0.1f);
        Assert.True(m.Skipped);
        m.Advance(BossIntroModel.SkipFade + 0.01f);
        Assert.False(m.Active);
        Assert.Equal(1, m.Skips);
    }

    [Fact]
    public void DefeatCardDipsTimeForFourTenthsOfASecond()
    {
        var m = new BossIntroModel();
        m.Defeat("king_slime", "King Slime", "#4f8cff", 5f, slowMotion: true);
        Assert.Equal(BossCardKind.Defeated, m.Kind);
        Assert.Equal("Defeated", m.Subtitle);
        Assert.Equal(0f, m.Framing);
        Assert.True(m.Dipping);
        Assert.Equal(BossIntroModel.DipScale, m.TimeScale, 4);
        m.Advance(BossIntroModel.DipHold);
        Assert.Equal(BossIntroModel.DipScale, m.TimeScale, 4);
        m.Advance((BossIntroModel.DipSeconds - BossIntroModel.DipHold) / 2);
        Assert.InRange(m.TimeScale, BossIntroModel.DipScale + 0.1f, 0.9f);
        m.Advance(BossIntroModel.DipSeconds);
        Assert.Equal(1f, m.TimeScale);
        Assert.False(m.Dipping);
        // Its bars are capped short, and it ends after its own (shorter) hold.
        Assert.Equal(BossIntroModel.FadeIn + BossIntroModel.DefeatHold + BossIntroModel.FadeOut, m.Duration, 4);
        Run(m, BossIntroModel.DefeatLetterbox + BossIntroModel.BarsOut - m.Time + 0.02f);
        Assert.Equal(0f, m.Bars);
        Run(m, m.Duration - m.Time + 0.02f);
        Assert.False(m.Active);

        var calm = new BossIntroModel();
        calm.Defeat("king_slime", "King Slime", "#4f8cff", 1f, slowMotion: false);
        Assert.Equal(1f, calm.TimeScale);
    }

    [Fact]
    public void NewCardReplacesTheOneShowing()
    {
        var m = Awoken();
        m.Advance(1f);
        m.Skip();
        m.Defeat("skeletron", "Skeletron", "#e0d6bc", 1f, slowMotion: true);
        Assert.False(m.Skipped);
        Assert.Equal(0f, m.Time);
        Assert.Equal(2, m.Shown);
        Assert.Equal("Defeated", m.Subtitle);
        m.Clear();
        Assert.False(m.Active);
        Assert.Equal(1f, m.TimeScale);
    }

    [Fact]
    public void EveryProgressionBossHasAValidIntro()
    {
        var bosses = TestPaths.Content.Enemies.All.Where(e => e.Boss is { Minor: false, Flag: not null, Music: not null }).ToList();
        Assert.NotEmpty(bosses);
        // Each defeat flag's group has one card (the Twins' on Retinazer, Golem's on the body, Moon Lord's on the core).
        foreach (var group in bosses.GroupBy(e => e.Boss!.Flag))
        {
            var intros = group.Where(e => e.Boss!.Intro != null).ToList();
            Assert.True(intros.Count == 1, $"{group.Key}: {intros.Count} intro cards");
            var intro = intros[0].Boss!.Intro!;
            Assert.False(string.IsNullOrWhiteSpace(intro.Subtitle));
            Assert.InRange(intro.Framing, 0f, BossIntroModel.MaxFramingDistance);
            Assert.InRange(intro.Letterbox, 0f, BossIntroModel.MaxLetterbox);
        }
    }
}
