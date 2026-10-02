using Godot;
using Strata.Core.Commands;
using Strata.Core.Diagnostics;
using Strata.Game.Bootstrap;
using Strata.Game.UI;

namespace Strata.Game.Debug;

/// <summary>
/// Probes on the global UI look (P2-01): the shipped font is ThemeDB's fallback, an unthemed panel resolves to
/// Terraria's blue, the exported theme file matches the code-built theme, and the effective UI scale; and on the
/// generated pixel sprites and buff/event icons the HUD preloads (P2-02, P2-03); on the enemy mini health bars; and on
/// the floating combat numbers (P2-19).
/// </summary>
[ProbeSource]
public static class UiProbes
{
    public static void Register(CommandContext ctx, LaunchedWorld world, GodotLog log)
    {
        ctx.AddProbe("ui.font", () => ThemeDB.FallbackFont?.ResourcePath == UiFonts.BoldPath ? 1 : 0);
        ctx.AddProbe("ui.theme", () =>
        {
            // A bare control with no theme of its own sees only the global themes, as every CanvasLayer's UI does.
            var probe = new PanelContainer();
            bool blue = probe.GetThemeStylebox("panel") is StyleBoxFlat flat && flat.BgColor.IsEqualApprox(UiTheme.PanelBlue);
            probe.Free();
            return blue ? 1 : 0;
        });
        ctx.AddProbe("ui.theme_synced", () =>
        {
            if (UiTheme.ExportInSync(out var difference)) return 1;
            log.Warn("ui", $"{UiTheme.ThemePath} is stale ({difference}); run: godot --headless --path . -- --export-theme");
            return 0;
        });
        ctx.AddProbe("ui.scale", () => world.Runtime.GetTree().Root.ContentScaleFactor);
        ctx.AddProbe("ui.warmup_ms", () => UiTheme.WarmupMs);
        ctx.AddProbe("ui.sprites", () => UiSprites.LoadedCount);
        ctx.AddProbe("ui.sprites_missing", () => UiSprites.MissingCount);
        ctx.AddProbe("ui.status_icons_missing", () => StatusIcons.MissingCount);
        ctx.AddProbe("hud.buff_icons", () => world.Hud?.BuffIconsShown ?? 0);
        Entities.NpcRenderer? creatures = null;
        ctx.AddProbe("fx.health_bars", () =>
        {
            creatures ??= world.Runtime.GetNodeOrNull<Entities.NpcRenderer>("Creatures");
            return creatures?.HealthBarsShown ?? 0;
        });
        Entities.CombatEffects? effects = null;
        Entities.CombatEffects? Effects() => effects ??= world.Runtime.GetNodeOrNull<Entities.CombatEffects>("CombatEffects");
        ctx.AddProbe("fx.numbers", () => Effects()?.NumbersShown ?? 0);
        ctx.AddProbe("fx.heal_numbers", () => Effects()?.HealNumbersShown ?? 0);
        ctx.AddProbe("fx.number_labels", () => Effects()?.NumberLabels ?? 0);
    }
}
