using Godot;
using Strata.Core.Commands;
using Strata.Core.Diagnostics;
using Strata.Game.Bootstrap;
using Strata.Game.UI;

namespace Strata.Game.Debug;

/// <summary>
/// Probes on the global UI look (P2-01): the shipped font is ThemeDB's fallback, an unthemed panel resolves to
/// Terraria's blue, the exported theme file matches the code-built theme, and the effective UI scale; and on the
/// generated pixel sprites the HUD preloads (P2-02).
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
    }
}
