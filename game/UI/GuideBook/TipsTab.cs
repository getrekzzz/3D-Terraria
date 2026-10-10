using System.Collections.Generic;
using System.Linq;
using Godot;
using Strata.Core.Content.Defs;
using Strata.Game.Settings;

namespace Strata.Game.UI.GuideBook;

/// <summary>
/// Tips & controls (P5-32): the Guide's tips grouped by topic. Text shows the current key bindings (rebinding shows
/// at once); a controls tip also names its action's keys.
/// </summary>
public sealed class TipsTab : ListTab<GuideTipDef>
{
    public override string Key => "tips";
    public override string Title => "Tips";
    protected override string Intro => "Tips from the Guide, with your own key bindings.";

    protected override IEnumerable<GuideTipDef> Entries() =>
        Ctx.Ui.Game.Session.Content.GuideTips.All.OrderBy(t => t.Topic).ThenBy(t => t.DisplayName);

    protected override string Label(GuideTipDef t) =>
        t.Topic == GuideTipTopic.Controls && t.Action != null ? $"{t.DisplayName}  [{KeyBindings.Label(t.Action)}]" : $"{t.Topic}: {t.DisplayName}";

    protected override void Render(GuideTipDef t, VBoxContainer page)
    {
        page.AddChild(GuidePage.Section(t.DisplayName));
        page.AddChild(GuidePage.Text(t.Topic.ToString(), UiTheme.Muted, 14));
        page.AddChild(GuidePage.Text(HintToast.Format(t.Text), null, 18));
        if (t.Action != null) page.AddChild(GuidePage.Text($"Bound to: {KeyBindings.Label(t.Action)} (change it in Settings → Controls)", UiTheme.Muted, 14));
    }
}
