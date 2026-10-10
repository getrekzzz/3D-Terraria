using System.Collections.Generic;
using System.Linq;
using Godot;
using Strata.Core.Conditions;
using Strata.Core.Content;
using Strata.Core.Content.Defs;
using Strata.Core.Gameplay.Town;
using Strata.Game.Items;

namespace Strata.Game.UI.GuideBook;

/// <summary>
/// The NPCs tab (P5-32): every townsperson with portrait and status (Living here / Homeless / Dead until dawn /
/// Bound somewhere / Not yet), what brings them (arrival conditions, or where to rescue them), what they love and
/// hate, how they feel now, their services and their whole shop with prices and stock conditions.
/// </summary>
public sealed class NpcsTab : ListTab<TownNpcDef>
{
    public override string Key => "npcs";
    public override string Title => "NPCs";
    protected override string Intro => "Everyone who may live in your town, and what brings them.";

    protected override IEnumerable<TownNpcDef> Entries() =>
        Ctx.Ui.Game.Session.Content.TownNpcs.All.OrderBy(n => n.Kind).ThenBy(n => n.DisplayName);

    protected override string Label(TownNpcDef n) => $"{n.DisplayName} — {Status(n)}";
    protected override Color? Tint(TownNpcDef n) => Ctx.Ui.Game.Town.Find(n.Id) is { CountsAsResident: true } ? UiTheme.Good : UiTheme.Muted;
    protected override Texture2D? Icon(TownNpcDef n) => PortraitIcons.TownNpc(n);

    private string Status(TownNpcDef n)
    {
        var npc = Ctx.Ui.Game.Town.Find(n.Id);
        if (npc == null) return n.Kind == TownNpcKind.Landmark ? "Not visiting" : "Not yet";
        if (npc.Bound) return "Bound somewhere";
        if (!npc.Alive) return "Dead until dawn";
        if (n.Kind == TownNpcKind.Landmark) return "Visiting";
        return npc.Home != null ? "Living here" : "Homeless";
    }

    /// <summary>The selected resident's title label (probe ui.book_title_width).</summary>
    public Label? TitleLabel { get; private set; }

    /// <summary>Width of the selected resident's title in pixels, -1 when none.</summary>
    public float TitleWidth => TitleLabel is { } t && GodotObject.IsInstanceValid(t) ? t.Size.X : -1f;

    protected override void Render(TownNpcDef n, VBoxContainer page)
    {
        var game = Ctx.Ui.Game;
        var content = game.Session.Content;
        var head = new HBoxContainer();
        head.AddThemeConstantOverride("separation", 12);
        head.AddChild(new TextureRect
        {
            Texture = PortraitIcons.TownNpc(n), CustomMinimumSize = new Vector2(96, 96),
            ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize, StretchMode = TextureRect.StretchModeEnum.KeepAspectCentered,
        });
        // The titles take the rest of the row: a shrink-centred box would get no width and wrap a letter per line.
        var titles = new VBoxContainer
        {
            SizeFlagsHorizontal = Control.SizeFlags.ExpandFill, SizeFlagsVertical = Control.SizeFlags.ShrinkCenter,
            CustomMinimumSize = new Vector2(200, 0),
        };
        var npc = game.Town.Find(n.Id);
        titles.AddChild(TitleLabel = GuidePage.Text(npc is { Bound: false } ? npc.Title : n.DisplayName, UiTheme.Text, 26));
        titles.AddChild(GuidePage.Text(Status(n), UiTheme.Muted));
        head.AddChild(titles);
        page.AddChild(head);
        if (n.Description is { } d) page.AddChild(GuidePage.Text(d, UiTheme.Muted));

        page.AddChild(GuidePage.Section("Moves in"));
        if (n.Starter) page.AddChild(GuidePage.Text("Here from the start."));
        if (n.Rescue is { } rescue)
        {
            var where = ConditionDescriber.Describe(rescue.Where, content);
            var req = ConditionDescriber.Describe(rescue.Requires, content);
            page.AddChild(GuidePage.Text($"Rescue them: found {rescue.Pose}" + (where.Length > 0 ? " " + where : " somewhere in the world") + (req.Length > 0 ? ", " + req : "") + "."));
        }
        var arrival = ConditionDescriber.Describe(n.Arrival, content);
        if (!n.Starter) page.AddChild(GuidePage.Text(arrival.Length > 0 ? $"Arrives with a free house once: {arrival}." : "Arrives once a house is free."));

        if (n.Mood.Length > 0)
        {
            page.AddChild(GuidePage.Section("Likes and dislikes"));
            foreach (var group in n.Mood.GroupBy(m => m.Score).OrderByDescending(g => g.Key))
                page.AddChild(GuidePage.Text($"{Feeling(group.Key)}: {string.Join(", ", group.Select(m => MoodSubject(m, content)))}",
                    group.Key > 0 ? UiTheme.Good : UiTheme.Bad));
        }
        if (npc is { CountsAsResident: true })
            page.AddChild(GuidePage.Text("“" + game.Commerce.DescribeHappiness(npc) + "”", UiTheme.Muted));

        if (n.AllServices.Length > 0)
        {
            page.AddChild(GuidePage.Section("Services"));
            page.AddChild(GuidePage.Text(string.Join(", ", n.AllServices.Select(s => Humanize(s.Type)))));
        }
        if (n.Shop.Length > 0)
        {
            page.AddChild(GuidePage.Section($"Shop ({n.Shop.Length})"));
            foreach (var entry in n.Shop)
            {
                if (!content.Items.TryGet(entry.Item, out var item)) continue;
                var cond = ConditionDescriber.Describe(entry.Conditions, content);
                string price = entry.Currency != null && content.Items.TryGet(entry.Currency, out var cur)
                    ? $"{entry.Price ?? 1} {cur.DisplayName}"
                    : ConditionDescriber.Coins(entry.Price ?? item.Value);
                string text = $"{item.DisplayName}  —  {price}" + (entry.Tab != ShopTabs.Shop ? $" ({entry.Tab})" : "") + (cond.Length > 0 ? " · " + cond : "");
                page.AddChild(GuidePage.Link(text, ItemIcons.Get(item), () => Ctx.Book.OpenEntry(item.Id), text));
            }
        }
    }

    private static string Feeling(int score) => score switch { >= 2 => "Loves", 1 => "Likes", -1 => "Dislikes", _ => score <= -2 ? "Hates" : "Neutral" };

    private static string MoodSubject(MoodEntry m, ContentDatabase content) =>
        m.Biome != null ? (content.Biomes.TryGet(m.Biome, out var b) ? b.DisplayName : Humanize(m.Biome))
        : m.Neighbor != null ? (content.TownNpcs.TryGet(m.Neighbor, out var t) ? t.DisplayName : Humanize(m.Neighbor))
        : m.Layer != null ? "the " + Humanize(m.Layer) : "?";

    private static string Humanize(string id)
    {
        var s = ConditionDescriber.Humanize(System.Text.RegularExpressions.Regex.Replace(id, "([a-z])([A-Z])", "$1 $2"));
        return s.Length > 0 ? char.ToUpperInvariant(s[0]) + s[1..] : s;
    }
}
