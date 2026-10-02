using Strata.Core.Commands;
using Strata.Game.Bootstrap;

namespace Strata.Game.Debug;

/// <summary>
/// Probes on what is under the crosshair (P1-07): the creature or resident hovered, who right-click would talk to, the
/// town dialog, and the crosshair label's text.
/// </summary>
[ProbeSource]
public static class HoverProbes
{
    public static void Register(CommandContext ctx, LaunchedWorld world)
    {
        if (world.Interaction is not { } interaction) return; // free-camera worlds have no crosshair
        var content = world.Runtime.Game.Session.Content;
        ctx.AddProbe("hover.npc", () => interaction.Hovered?.Npc != null ? 1 : 0);
        ctx.AddProbe("hover.resident", () => interaction.Hovered?.Resident != null ? 1 : 0);
        ctx.AddProbe("hover.talk", () => interaction.TalkTarget != null ? 1 : 0); // a resident within talking range
        // hover.<enemy id>: 1 when the hovered creature (a worm segment counts as its worm) is of that kind.
        ctx.AddPrefixProbe("hover.", id =>
            content.Enemies.Contains(id) ? interaction.Hovered?.Npc?.Def.Id == id ? 1 : 0 : null);
        if (world.TownDialog is { } dialog) ctx.AddProbe("ui.talk_open", () => dialog.IsOpen ? 1 : 0);
        if (world.Hud is not { } hud) return;
        // hud.target_name: 1 while the crosshair label shows a name; hud.target_name.<words>: 1 when its text, lower case
        // with spaces as underscores ("blue_slime_25/25"), contains the words (hud.target_name.the_guide).
        ctx.AddProbe("hud.target_name", () => hud.TargetText.Length > 0 ? 1 : 0);
        ctx.AddPrefixProbe("hud.target_name.", words =>
            hud.TargetText.ToLowerInvariant().Replace(' ', '_').Contains(words.ToLowerInvariant(), System.StringComparison.Ordinal) ? 1 : 0);
    }
}
