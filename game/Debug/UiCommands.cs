using Godot;
using Strata.Core.Commands;
using Strata.Game.Bootstrap;
using Strata.Game.Settings;
using Strata.Game.UI;

namespace Strata.Game.Debug;

/// <summary>Commands for the interface: opening gameplay screens, the UI scale, and exporting the theme resource.</summary>
public static class UiCommands
{
    public static void Register(CommandRegistry r, LaunchedWorld world)
    {
        r.Register(new CommandDef
        {
            Name = "ui", Usage = "ui <inventory|map|talk <resident>|shop|close|export-theme|" + TownUiCommands.Usage + ">",
            Help = "Opens or closes gameplay screens; export-theme rewrites assets/ui/strata_theme.tres from the code-built theme.",
            Run = (ctx, a) =>
            {
                string what = a.Str(0, "screen");
                if (what == "export-theme")
                {
                    var error = UiTheme.Export();
                    return error == Error.Ok ? CommandResult.Success($"Theme saved to {UiTheme.ThemePath}.") : CommandResult.Fail($"Could not save the theme: {error}.");
                }
                var screen = world.Inventory ?? throw new CommandException("no inventory screen in this mode");
                var dialog = world.TownDialog ?? throw new CommandException("no town dialog in this mode");
                switch (what)
                {
                    case "inventory": screen.SetOpen(true); return CommandResult.Success("Inventory open.");
                    case "map":
                        (world.WorldMap ?? throw new CommandException("no map in this mode")).SetOpen(true);
                        return CommandResult.Success("Map open.");
                    case "talk":
                        var id = a.Str(1, "resident");
                        var npc = ctx.World.Town.Find(id) ?? throw new CommandException($"'{id}' does not live in town");
                        if (!ctx.World.Town.BeginTalk(npc, ctx.Player)) return CommandResult.Fail($"{npc.Title} is too far away.");
                        dialog.Open(npc);
                        return CommandResult.Success($"Talking to {npc.Title}.");
                    case "shop":
                        if (!dialog.IsOpen) return CommandResult.Fail("Not talking to anyone.");
                        dialog.OpenShop();
                        return CommandResult.Success("Shop open.");
                    case "close":
                        screen.SetOpen(false);
                        dialog.Close();
                        world.WorldMap?.SetOpen(false);
                        world.TownPanels?.CloseAll();
                        return CommandResult.Success("Closed.");
                    default: return TownUiCommands.TryRun(ctx, a, world) ?? throw new CommandException("expected inventory, map, talk, shop, close, export-theme or " + TownUiCommands.Usage);
                }
            },
        });
        r.Register(new CommandDef
        {
            Name = "uiscale", Usage = "uiscale <factor>",
            Help = "Sets the UI scale for this session (not saved); the window size may lower the effective scale (probe ui.scale).",
            Run = (_, a) =>
            {
                float factor = a.Float(0, "factor");
                if (factor is < 0.5f or > 3f) throw new CommandException("factor must be within 0.5..3");
                UserSettings.Current.UiScale = factor;
                DisplaySettings.ApplyUiScale(world.Runtime.GetTree().Root, factor);
                return CommandResult.Success(DisplaySettings.UiScaleClamped
                    ? $"UI scale {factor:0.##} (effective {DisplaySettings.EffectiveUiScale:0.##}: the window is too small)."
                    : $"UI scale {DisplaySettings.EffectiveUiScale:0.##}.");
            },
        });
    }
}
