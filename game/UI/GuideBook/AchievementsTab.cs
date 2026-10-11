using System;
using System.Collections.Generic;
using System.Linq;
using Godot;
using Strata.Core.Content.Defs;
using Strata.Game.Items;

namespace Strata.Game.UI.GuideBook;

/// <summary>Achievements (P5-32): every achievement by category, unlocked ones gold with the date, the rest grey with a
/// progress bar from <see cref="Strata.Core.Gameplay.Guide.AchievementTracker.Progress"/>.</summary>
public sealed class AchievementsTab : ListTab<AchievementDef>
{
    public override string Key => "achievements";
    public override string Title => "Achievements";

    protected override string Intro
    {
        get
        {
            var j = Ctx.Ui.Entity.State.Journal;
            return $"{j.Achievements.Count} of {Ctx.Ui.Game.Session.Content.Achievements.Count} achievements unlocked.";
        }
    }

    protected override IEnumerable<AchievementDef> Entries() =>
        Ctx.Ui.Game.Session.Content.Achievements.All.OrderBy(a => a.Category).ThenBy(a => a.DisplayName);

    private bool Done(AchievementDef a) => Ctx.Ui.Entity.State.Journal.Achieved(a.Id);

    protected override string Label(AchievementDef a) => (Done(a) ? "★ " : "") + a.DisplayName;
    protected override Color? Tint(AchievementDef a) => Done(a) ? UiTheme.Gold : UiTheme.Muted;

    protected override Texture2D? Icon(AchievementDef a) =>
        a.Icon != null && Ctx.Ui.Game.Session.Content.Items.TryGet(a.Icon, out var item) ? ItemIcons.Get(item) : null;

    protected override void Render(AchievementDef a, VBoxContainer page)
    {
        var j = Ctx.Ui.Entity.State.Journal;
        page.AddChild(GuidePage.Section(a.DisplayName));
        page.AddChild(GuidePage.Text(a.Category.ToString(), UiTheme.Muted, 14));
        page.AddChild(GuidePage.Text(a.Description ?? "", null, 18));
        if (j.Achievements.TryGetValue(a.Id, out long when))
        {
            page.AddChild(GuidePage.Text("Unlocked " + DateTimeOffset.FromUnixTimeSeconds(when).LocalDateTime.ToString("d MMM yyyy"), UiTheme.Gold));
            return;
        }
        var (cur, target) = Ctx.Ui.Game.Achievements.Progress(a, Ctx.Ui.Entity);
        var bar = new ProgressBar { MinValue = 0, MaxValue = Math.Max(1, target), Value = cur, ShowPercentage = false, CustomMinimumSize = new Vector2(0, 18) };
        page.AddChild(bar);
        page.AddChild(GuidePage.Text($"{cur} / {target}", UiTheme.Muted, 14));
    }
}
