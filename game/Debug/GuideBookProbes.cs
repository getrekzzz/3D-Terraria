using Strata.Core.Commands;
using Strata.Game.Bootstrap;

namespace Strata.Game.Debug;

/// <summary>
/// Guide book probes: <c>ui.book_open</c>, <c>ui.book_ready</c> (the Items index finished building),
/// <c>ui.book_tab</c> (0 items, 1 bestiary, 2 guide, -1 closed), <c>ui.book_sources</c> ("Obtained from" rows of the
/// shown item), <c>ui.book_items</c> (items listed), <c>ui.bestiary_unlock</c> (0–3 for the shown entry, -1 none) and
/// <c>ui.book_guide</c> (1 while the Guide tab shows an entry).
/// </summary>
[ProbeSource]
public static class GuideBookProbes
{
    public static void Register(CommandContext ctx, LaunchedWorld world)
    {
        ctx.AddProbe("ui.book_open", () => GuideBookCommands.Find(world)?.IsOpen == true ? 1 : 0);
        ctx.AddProbe("ui.book_ready", () => GuideBookCommands.Find(world)?.IndexReady == true ? 1 : 0);
        ctx.AddProbe("ui.book_tab", () => GuideBookCommands.Find(world) is { IsOpen: true, Current: { } t } b
            ? t == b.Items ? 0 : t == b.Bestiary ? 1 : 2 : -1);
        ctx.AddProbe("ui.book_sources", () => GuideBookCommands.Find(world)?.Items is { Selected: not null } i ? i.SourceCount : -1);
        ctx.AddProbe("ui.book_items", () => GuideBookCommands.Find(world)?.Items.ListedCount ?? 0);
        ctx.AddProbe("ui.bestiary_unlock", () => GuideBookCommands.Find(world)?.Bestiary.ShownUnlock ?? -1);
        ctx.AddProbe("ui.book_guide", () => GuideBookCommands.Find(world)?.Guide.Selected != null ? 1 : 0);
    }
}
