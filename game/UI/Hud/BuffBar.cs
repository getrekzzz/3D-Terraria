using System.Collections.Generic;
using Godot;
using Strata.Core.Content.Defs;
using Strata.Core.Gameplay.Buffs;
using Strata.Core.Presentation;

namespace Strata.Game.UI;

/// <summary>
/// The buff bar under the hotbar, as in Terraria: the active buffs in the order they came, then one icon per minion
/// kind summoned (with its count) and the minecart while riding, wrapping in rows across the Buffs region (up to 44).
/// Its pooled <see cref="BuffIconView"/>s are rebound only when the buff set changes (<see cref="BuffSet.Changed"/>),
/// the buff count does, or the minions or mount do; each frame only advances their countdowns. With a screen open
/// (the pointer free) the icons take the mouse: a right click, or <c>ui buff cancel</c>, cancels a buff (never a
/// debuff) or sends that kind of minion home.
/// </summary>
public sealed partial class BuffBar : Control
{
    private const int MinionScanFrames = 6;

    private readonly UiContext _ui;
    private readonly BuffIconView[] _views = new BuffIconView[UiLayout.MaxBuffs];
    private readonly List<(ProjectileDef Def, int Count)> _minions = new(), _minionsShown = new();
    private readonly Dictionary<ProjectileDef, ItemDef?> _staffs = new();
    private readonly ItemDef? _cart;
    private BuffSet? _set;
    private int _created, _buffCount = -1, _scan;
    private bool _dirty = true, _riding, _mouseFree;

    public BuffBar(UiContext ui)
    {
        _ui = ui;
        Name = "Buffs";
        MouseFilter = MouseFilterEnum.Ignore;
        ui.Game.Session.Content.Items.TryGet("minecart", out _cart);
    }

    /// <summary>Icons shown: buffs, minion kinds and the mount (the layout's buff count; the <c>hud.buffs</c> probe).</summary>
    public int Count { get; private set; }

    /// <summary>Buffs shown with their generated icon (the <c>hud.buff_icons</c> probe).</summary>
    public int IconsShown { get; private set; }

    /// <summary>Brings the icons up to date; the HUD calls it each frame before laying out (it reads <see cref="Count"/>).</summary>
    public void Refresh(double delta)
    {
        var set = _ui.Entity.State.Buffs;
        if (set != _set)
        {
            if (_set != null) _set.Changed -= MarkDirty;
            _set = set;
            set.Changed += MarkDirty;
            _dirty = true;
        }
        if (set.Active.Count != _buffCount) _dirty = true;
        if (--_scan <= 0) ScanMinions();
        bool riding = _ui.Entity.Minecart != null;
        if (riding != _riding)
        {
            _riding = riding;
            _dirty = true;
        }
        if (_dirty) Rebind();
        bool free = InputGate.MouseFree;
        if (free != _mouseFree)
        {
            _mouseFree = free;
            for (int i = 0; i < Count; i++) _views[i].MouseFilter = free ? MouseFilterEnum.Stop : MouseFilterEnum.Ignore;
        }
        for (int i = 0; i < Count; i++) _views[i].Tick(delta);
    }

    /// <summary>The icon <c>ui buff cancel</c> names (a buff id, a minion's projectile id or "minecart"), or null.</summary>
    public BuffIconView? Find(string id)
    {
        for (int i = 0; i < Count; i++)
            if (_views[i].Id == id) return _views[i];
        return null;
    }

    /// <summary>
    /// The icon was right-clicked: cancels the buff (refused for debuffs and buffs that cannot be shed) and recomputes
    /// the stats, or sends that kind of minion home. Returns whether anything went.
    /// </summary>
    public bool Cancel(BuffIconView view)
    {
        bool done = false;
        if (view.Kind == BuffIconKind.Buff)
        {
            var state = _ui.Entity.State;
            done = state.Buffs.TryCancel(view.Buff!.Def.Id);
            if (done) state.Recompute();
        }
        else if (view.Kind == BuffIconKind.Minion)
        {
            done = _ui.Game.Projectiles.Unsummon(_ui.Entity, view.Minion!) > 0;
            if (done) ScanMinions();
        }
        if (!done) return false;
        TooltipFollower.Current?.Hide(view);
        Audio.Sfx.Play("ui_click");
        _dirty = true;
        return true;
    }

    /// <summary>The icons in rows across the region, as many per row as it is wide (after each layout and rebind).</summary>
    public void Arrange()
    {
        int perRow = Mathf.Max(1, (int)((Size.X + UiLayout.BuffPitchX - UiLayout.BuffIcon + 0.5f) / UiLayout.BuffPitchX));
        for (int i = 0; i < Count; i++) _views[i].Position = new Vector2(i % perRow * UiLayout.BuffPitchX, i / perRow * UiLayout.BuffPitchY);
    }

    private void MarkDirty() => _dirty = true;

    /// <summary>Counts the minions by kind a few times a second; a change in kinds or counts rebinds.</summary>
    private void ScanMinions()
    {
        _scan = MinionScanFrames;
        _ui.Game.Projectiles.CountMinions(_ui.Entity, _minions);
        bool same = _minions.Count == _minionsShown.Count;
        for (int i = 0; same && i < _minions.Count; i++) same = _minions[i] == _minionsShown[i];
        if (same) return;
        _minionsShown.Clear();
        _minionsShown.AddRange(_minions);
        _dirty = true;
    }

    private void Rebind()
    {
        _dirty = false;
        var active = _ui.Entity.State.Buffs.Active;
        _buffCount = active.Count;
        int n = 0, icons = 0;
        for (int i = 0; i < active.Count && n < _views.Length; i++)
        {
            var view = View(n++);
            view.BindBuff(active[i]);
            if (view.HasBuffIcon) icons++;
        }
        for (int i = 0; i < _minionsShown.Count && n < _views.Length; i++)
            View(n++).BindMinion(_minionsShown[i].Def, Staff(_minionsShown[i].Def), _minionsShown[i].Count);
        if (_riding && n < _views.Length) View(n++).BindMount(_cart);
        for (int i = 0; i < _created; i++)
        {
            bool shown = i < n;
            if (_views[i].Visible == shown) continue;
            _views[i].Visible = shown;
            if (!shown) TooltipFollower.Current?.Hide(_views[i]);
        }
        Count = n;
        IconsShown = icons;
        Arrange();
    }

    /// <summary>The pooled view <paramref name="index"/>, created on first use, taking the mouse only while it is free.</summary>
    private BuffIconView View(int index)
    {
        if (index < _created) return _views[index];
        var view = new BuffIconView(_ui.Game.Session.Content) { Name = $"Buff{index}" };
        view.MouseFilter = _mouseFree ? MouseFilterEnum.Stop : MouseFilterEnum.Ignore;
        view.Cancelled += v => Cancel(v);
        AddChild(view);
        _views[index] = view;
        _created = index + 1;
        return view;
    }

    /// <summary>The item that summons a minion kind (its icon stands for the minion buff), looked up once per kind.</summary>
    private ItemDef? Staff(ProjectileDef minion)
    {
        if (_staffs.TryGetValue(minion, out var staff)) return staff;
        foreach (var item in _ui.Game.Session.Content.Items.All)
            if (item.Projectile == minion.Id)
            {
                staff = item;
                break;
            }
        _staffs[minion] = staff;
        return staff;
    }
}
