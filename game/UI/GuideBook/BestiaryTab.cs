using System.Collections.Generic;
using System.Globalization;
using Godot;
using Strata.Core.Content;
using Strata.Core.Content.Defs;
using Strata.Core.Gameplay.Guide;
using Strata.Game.Items;

namespace Strata.Game.UI.GuideBook;

/// <summary>
/// The Bestiary tab (P5-31): every creature and resident, "???" until seen, with filters (All, Unlocked, Bosses,
/// Critters, Town) and search, completion % in the header, portraits loaded a few per frame, and the selected entry's
/// page (<see cref="BestiaryDetails"/>).
/// </summary>
public sealed class BestiaryTab : IGuideTab
{
    private const int IconsPerFrame = 8;
    private static readonly string[] Filters = { "All", "Unlocked", "Bosses", "Critters", "Town" };

    private GuideBookContext _ctx = null!;
    private LineEdit _search = null!;
    private OptionButton _filter = null!;
    private Label _header = null!;
    private ItemList _list = null!;
    private BestiaryDetails? _details;
    private readonly List<ContentDef> _shown = new();
    private int _nextIcon;

    public string Key => "bestiary";
    public string Title => "Bestiary";

    /// <summary>Unlock tier of the shown entry, 0–3 (probe ui.bestiary_unlock); -1 when none.</summary>
    public int ShownUnlock => _details?.Entry == null ? -1 : (int)_details.Unlock;

    private Bestiary Book => _ctx.Ui.Game.Journal.Bestiary;
    private PlayerJournal Journal => _ctx.Ui.Entity.State.Journal;

    public Control Build(GuideBookContext ctx)
    {
        _ctx = ctx;
        var root = new Ticker { Tick = Tick };
        var split = new HBoxContainer();
        split.SetAnchorsPreset(Control.LayoutPreset.FullRect);
        split.AddThemeConstantOverride("separation", 16);
        var left = new VBoxContainer { CustomMinimumSize = new Vector2(340, 0) };
        _header = GuidePage.Section("");
        _search = new LineEdit { PlaceholderText = "Search creatures…", ClearButtonEnabled = true };
        _search.TextChanged += _ => Refill();
        _search.FocusEntered += () => InputGate.TextEntry = true;
        _search.FocusExited += () => InputGate.TextEntry = false;
        _filter = new OptionButton { FocusMode = Control.FocusModeEnum.None };
        foreach (var f in Filters) _filter.AddItem(f);
        _filter.ItemSelected += _ => Refill();
        _list = new ItemList { SizeFlagsVertical = Control.SizeFlags.ExpandFill, FixedIconSize = new Vector2I(28, 28) };
        _list.ItemSelected += i => _details!.Show(_shown[(int)i]);
        left.AddChild(_header);
        left.AddChild(_search);
        left.AddChild(_filter);
        left.AddChild(_list);
        split.AddChild(left);
        _details = new BestiaryDetails(ctx);
        split.AddChild(_details.Root);
        root.AddChild(split);
        return root;
    }

    public void Shown()
    {
        _header.Text = $"Bestiary — {Journal.BestiaryPercent.ToString("0.#", CultureInfo.InvariantCulture)}% complete";
        Refill();
        if (_details!.Entry != null) _details.Show(_details.Entry);
    }

    public bool Show(string id)
    {
        var content = _ctx.Ui.Game.Session.Content;
        ContentDef? entry = content.Enemies.TryGet(id, out var e) ? content.Enemies.Get(Bestiary.EntryOf(e))
            : content.TownNpcs.TryGet(id, out var t) ? t : null;
        if (entry == null || !Book.Contains(entry.Id)) return false;
        int at = _shown.IndexOf(entry);
        if (at < 0)
        {
            _search.Text = "";
            _filter.Selected = 0;
            Refill();
            at = _shown.IndexOf(entry);
        }
        if (at >= 0)
        {
            _list.Select(at);
            _list.EnsureCurrentIsVisible();
        }
        _details!.Show(entry);
        return true;
    }

    private void Refill()
    {
        string q = _search.Text.Trim().ToLowerInvariant();
        _shown.Clear();
        _list.Clear();
        foreach (var entry in Book.Entries)
        {
            var unlock = Book.Unlock(entry, Journal);
            bool known = unlock != BestiaryUnlock.None;
            bool keep = _filter.Selected switch
            {
                1 => known,
                2 => entry is EnemyDef { Boss: not null },
                3 => entry.HasTag("critter"),
                4 => entry is TownNpcDef,
                _ => true,
            };
            if (!keep || (q.Length > 0 && !(known && entry.DisplayName.ToLowerInvariant().Contains(q)))) continue;
            _shown.Add(entry);
            int i = _list.AddItem(known ? entry.DisplayName : "???");
            if (!known) _list.SetItemCustomFgColor(i, UiTheme.Muted);
        }
        _nextIcon = 0;
    }

    private void Tick()
    {
        if (!_list.IsVisibleInTree()) return;
        for (int n = 0; n < IconsPerFrame && _nextIcon < _shown.Count; n++, _nextIcon++)
        {
            var entry = _shown[_nextIcon];
            if (Book.Unlock(entry, Journal) == BestiaryUnlock.None) continue;
            var icon = entry switch { EnemyDef e => PortraitIcons.Creature(e), TownNpcDef t => PortraitIcons.TownNpc(t), _ => null };
            if (icon != null) _list.SetItemIcon(_nextIcon, icon);
        }
    }
}
