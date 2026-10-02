using System.Collections.Generic;
using Godot;
using Strata.Core.Content;
using Strata.Core.Content.Defs;

namespace Strata.Game.UI.GuideBook;

/// <summary>
/// The Guide tab: the Guide's progression steps (done ✓ / next ▶) and his tips, where onboarding hints link to
/// (X-10). The right page shows the selected step's or tip's text with key bindings filled in.
/// </summary>
public sealed class GuideTab : IGuideTab
{
    private GuideBookContext _ctx = null!;
    private ItemList _list = null!;
    private Label _title = null!;
    private Label _text = null!;
    private readonly List<ContentDef> _entries = new();

    public string Key => "guide";
    public string Title => "Guide";

    /// <summary>The entry shown on the right (null when none).</summary>
    public ContentDef? Selected { get; private set; }

    public Control Build(GuideBookContext ctx)
    {
        _ctx = ctx;
        var split = new HBoxContainer();
        split.AddThemeConstantOverride("separation", 16);
        _list = new ItemList { CustomMinimumSize = new Vector2(340, 0), SizeFlagsVertical = Control.SizeFlags.ExpandFill };
        _list.ItemSelected += i => Select(_entries[(int)i]);
        var right = new VBoxContainer { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
        _title = GuidePage.Section("The Guide");
        _text = GuidePage.Text("Steps of your journey and the Guide's tips. Pick one on the left.", UiTheme.Muted, 18);
        right.AddChild(_title);
        right.AddChild(_text);
        split.AddChild(_list);
        split.AddChild(right);
        return split;
    }

    public void Shown()
    {
        var game = _ctx.Ui.Game;
        _entries.Clear();
        _list.Clear();
        var steps = game.Advisor.Steps(_ctx.Ui.Entity);
        var next = game.Advisor.Next(_ctx.Ui.Entity);
        foreach (var s in steps)
        {
            string mark = s.Done ? "✓ " : next.Count > 0 && next[0] == s.Step ? "▶ " : "· ";
            int i = _list.AddItem(mark + s.Step.DisplayName);
            if (s.Done) _list.SetItemCustomFgColor(i, UiTheme.Good);
            else if (!s.Available) _list.SetItemCustomFgColor(i, UiTheme.Muted);
            _entries.Add(s.Step);
        }
        foreach (var tip in game.Session.Content.GuideTips.All)
        {
            _list.AddItem("Tip: " + tip.DisplayName);
            _entries.Add(tip);
        }
        if (Selected != null) Show(Selected.Id);
    }

    public bool Show(string id)
    {
        int at = _entries.FindIndex(e => e.Id == id);
        if (at < 0)
        {
            if (_entries.Count == 0) Shown();
            at = _entries.FindIndex(e => e.Id == id);
            if (at < 0) return false;
        }
        _list.Select(at);
        _list.EnsureCurrentIsVisible();
        Select(_entries[at]);
        return true;
    }

    private void Select(ContentDef entry)
    {
        Selected = entry;
        _title.Text = entry.DisplayName;
        _text.Text = HintToast.Format(entry switch { GuideStepDef s => s.Text, GuideTipDef t => t.Text, _ => "" });
    }
}
