using System;
using Godot;
using Strata.Core.Conditions;
using Strata.Core.Content;
using Strata.Core.Content.Defs;
using Strata.Core.Gameplay.Combat;
using Strata.Core.Gameplay.Guide;
using Strata.Core.Gameplay.Loot;
using Strata.Game.Items;

namespace Strata.Game.UI.GuideBook;

/// <summary>
/// A bestiary page by unlock tier: nothing but "???" until seen; then portrait, name and habitat (spawn places,
/// events, summon items); after a kill life, defence, damage, knockback resistance, coins (scaled by the world's
/// difficulty) and the description; fully unlocked, every drop with its chance. Residents show met state and when
/// they move in. The portrait is the generated creature thumbnail (no live 3D model yet).
/// </summary>
public sealed class BestiaryDetails
{
    private readonly GuideBookContext _ctx;
    private readonly VBoxContainer _page;

    public BestiaryDetails(GuideBookContext ctx)
    {
        _ctx = ctx;
        Root = new ScrollContainer { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill, HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled };
        _page = new VBoxContainer { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
        _page.AddThemeConstantOverride("separation", 6);
        Root.AddChild(_page);
        _page.AddChild(GuidePage.Text("Pick a creature on the left.", UiTheme.Muted));
    }

    public ScrollContainer Root { get; }
    public ContentDef? Entry { get; private set; }
    public BestiaryUnlock Unlock { get; private set; }

    public void Show(ContentDef entry)
    {
        Entry = entry;
        var game = _ctx.Ui.Game;
        var journal = _ctx.Ui.Entity.State.Journal;
        Unlock = game.Journal.Bestiary.Unlock(entry, journal);
        foreach (var c in _page.GetChildren()) c.QueueFree();
        if (Unlock == BestiaryUnlock.None)
        {
            _page.AddChild(GuidePage.Text("???", UiTheme.Muted, 28));
            _page.AddChild(GuidePage.Text(entry is TownNpcDef ? "You have not met this person yet." : "You have not seen this creature yet.", UiTheme.Muted));
            return;
        }
        var head = new HBoxContainer();
        head.AddThemeConstantOverride("separation", 12);
        head.AddChild(new TextureRect
        {
            Texture = entry switch { EnemyDef e => PortraitIcons.Creature(e), TownNpcDef t => PortraitIcons.TownNpc(t), _ => null },
            CustomMinimumSize = new Vector2(128, 128),
            ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize, StretchMode = TextureRect.StretchModeEnum.KeepAspectCentered,
        });
        var titles = new VBoxContainer { SizeFlagsVertical = Control.SizeFlags.ShrinkCenter };
        titles.AddChild(GuidePage.Text(entry.DisplayName, entry is EnemyDef { Boss: not null } ? UiTheme.BossPurple : UiTheme.Text, 26));
        if (entry is EnemyDef)
            titles.AddChild(GuidePage.Text($"Defeated: {game.Journal.Bestiary.Kills(entry.Id, journal)}", UiTheme.Muted));
        head.AddChild(titles);
        _page.AddChild(head);
        if (entry is TownNpcDef npc) ShowResident(npc, game.Session.Content);
        else ShowEnemy((EnemyDef)entry, game.Session.Content, game.Npcs.Scale);
    }

    private void ShowResident(TownNpcDef npc, ContentDatabase content)
    {
        if (npc.Description is { } d) _page.AddChild(GuidePage.Text(d));
        var arrive = ConditionDescriber.Describe(npc.Arrival, content);
        _page.AddChild(GuidePage.Section("Moves in"));
        _page.AddChild(GuidePage.Text(arrive.Length > 0 ? "Once " + arrive : "When a house is free.", UiTheme.Muted));
    }

    private void ShowEnemy(EnemyDef e, ContentDatabase content, float scale)
    {
        var habitat = Habitat.For(content, e);
        _page.AddChild(GuidePage.Section("Habitat"));
        if (habitat.Places.Count == 0 && habitat.Events.Count == 0 && habitat.SummonedBy.Count == 0)
            _page.AddChild(GuidePage.Text("Found only in special places.", UiTheme.Muted));
        foreach (var place in habitat.Places) _page.AddChild(GuidePage.Text(place, UiTheme.Muted));
        foreach (var ev in habitat.Events) _page.AddChild(GuidePage.Text("Ends the " + ev.DisplayName, UiTheme.Muted));
        foreach (var item in habitat.SummonedBy)
        {
            var it = item;
            _page.AddChild(GuidePage.Link("Summoned with " + item.DisplayName, ItemIcons.Get(item), () => _ctx.Book.OpenEntry(it.Id)));
        }
        if (Unlock < BestiaryUnlock.Stats) return;
        _page.AddChild(GuidePage.Section("Stats"));
        var coins = DropRates.Coins(content, e);
        _page.AddChild(GuidePage.Text(
            $"Life {(int)(e.Life * MathF.Max(1f, scale))}   Defense {e.Defense}   Damage {(int)(e.Damage * scale)}   " +
            $"Knockback resist {(int)(e.KnockbackResist * 100)}%" +
            (coins is { } c ? $"   Coins {ConditionDescriber.Coins(c.Min)}–{ConditionDescriber.Coins(c.Max)}" : "")));
        if (e.Description is { } d) _page.AddChild(GuidePage.Text(d, UiTheme.Muted));
        if (Unlock < BestiaryUnlock.Full || e.Loot == null) return;
        var drops = DropRates.For(content, e.Loot);
        _page.AddChild(GuidePage.Section("Drops"));
        if (drops.Count == 0) _page.AddChild(GuidePage.Text("Nothing.", UiTheme.Muted));
        foreach (var drop in drops)
        {
            var item = drop.Item;
            var cond = ConditionDescriber.Describe(drop.Conditions, content);
            string amount = drop.Max > 1 ? (drop.Min == drop.Max ? $" ×{drop.Min}" : $" ×{drop.Min}–{drop.Max}") : "";
            string text = $"{item.DisplayName}{amount}  —  {GuideIndex.Percent(drop.Chance)}" + (cond.Length > 0 ? " · " + cond : "");
            _page.AddChild(GuidePage.Link(text, ItemIcons.Get(item), () => _ctx.Book.OpenEntry(item.Id), text));
        }
    }
}
