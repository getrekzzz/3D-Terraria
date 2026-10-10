using System.Collections.Generic;
using Godot;
using Strata.Core.Content.Defs;
using Strata.Game.Items;

namespace Strata.Game.UI.GuideBook;

/// <summary>
/// The progression checklist (P5-32): the Guide's steps grouped by stage, ✓ when done, ▶ for what is next (gold),
/// grey while not yet offered; boss steps show the boss portrait. A step's page has its advice and chips linking to
/// its items (Items tab) and enemies (Bestiary).
/// </summary>
public sealed class ProgressTab : ListTab<GuideStepDef>
{
    private readonly Dictionary<GuideStepDef, (bool Done, bool Available)> _state = new();
    private GuideStepDef? _next;

    public override string Key => "progress";
    public override string Title => "Progress";
    protected override string Intro => "Your journey so far, in the order the Guide would take it. Pick a step for his advice.";

    protected override IEnumerable<GuideStepDef> Entries()
    {
        var steps = Ctx.Ui.Game.Advisor.Steps(Ctx.Ui.Entity);
        var next = Ctx.Ui.Game.Advisor.Next(Ctx.Ui.Entity);
        _next = next.Count > 0 ? next[0] : null;
        _state.Clear();
        var list = new List<GuideStepDef>(steps.Count);
        foreach (var s in steps)
        {
            _state[s.Step] = (s.Done, s.Available);
            list.Add(s.Step);
        }
        list.Sort((a, b) => a.Stage != b.Stage ? a.Stage.CompareTo(b.Stage) : a.Order.CompareTo(b.Order));
        return list;
    }

    protected override string Label(GuideStepDef s)
    {
        var (done, _) = _state[s];
        string mark = done ? "✓ " : s == _next ? "▶ " : "· ";
        return $"{mark}{s.DisplayName}";
    }

    protected override Color? Tint(GuideStepDef s)
    {
        var (done, available) = _state[s];
        return done ? UiTheme.Good : s == _next ? UiTheme.Gold : available ? null : UiTheme.Muted;
    }

    protected override Texture2D? Icon(GuideStepDef s)
    {
        var content = Ctx.Ui.Game.Session.Content;
        foreach (var id in s.RelatedEnemies)
            if (content.Enemies.TryGet(id, out var e)) return e.Boss != null ? PortraitIcons.Boss(e) ?? PortraitIcons.Creature(e) : PortraitIcons.Creature(e);
        foreach (var id in s.RelatedItems)
            if (content.Items.TryGet(id, out var i)) return ItemIcons.Get(i);
        return null;
    }

    protected override void Render(GuideStepDef s, VBoxContainer page)
    {
        var content = Ctx.Ui.Game.Session.Content;
        var (done, available) = _state.TryGetValue(s, out var st) ? st : (false, false);
        page.AddChild(GuidePage.Section(s.DisplayName));
        page.AddChild(GuidePage.Text(done ? "Done." : s == _next ? "Next up." : available ? "Open to you." : "Later.",
            done ? UiTheme.Good : s == _next ? UiTheme.Gold : UiTheme.Muted, 14));
        page.AddChild(GuidePage.Text(HintToast.Format(s.Text), null, 18));
        foreach (var id in s.RelatedItems)
            if (content.Items.TryGet(id, out var item))
                page.AddChild(GuidePage.Link(item.DisplayName, ItemIcons.Get(item), () => Ctx.Book.OpenEntry(item.Id)));
        foreach (var id in s.RelatedEnemies)
            if (content.Enemies.TryGet(id, out var e))
                page.AddChild(GuidePage.Link(e.DisplayName, PortraitIcons.Creature(e), () => Ctx.Book.OpenEntry(e.Id)));
    }
}
