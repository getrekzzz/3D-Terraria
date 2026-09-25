using Strata.Core.Commands;
using Strata.Game.Bootstrap;
using Strata.Game.UI;

namespace Strata.Game.Debug;

/// <summary>Commands that need the engine layer (UI screens, camera modes).</summary>
public static class EngineCommands
{
    public static void Register(CommandRegistry r, LaunchedWorld world)
    {
        r.Register(new CommandDef
        {
            Name = "ui", Usage = "ui <inventory|talk <resident>|shop|close>", Help = "Opens or closes gameplay screens.",
            Run = (ctx, a) =>
            {
                var screen = world.Inventory ?? throw new CommandException("no inventory screen in this mode");
                var dialog = world.TownDialog ?? throw new CommandException("no town dialog in this mode");
                switch (a.Str(0, "screen"))
                {
                    case "inventory": screen.SetOpen(true); return CommandResult.Success("Inventory open.");
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
                        return CommandResult.Success("Closed.");
                    default: throw new CommandException("expected inventory, talk, shop or close");
                }
            },
        });
        r.Register(new CommandDef
        {
            Name = "camera", Usage = "camera <first|third>", Help = "Switches the player camera mode.",
            Run = (ctx, a) =>
            {
                var player = world.Player ?? throw new CommandException("no player camera in this mode");
                player.Rig.FirstPerson = a.Str(0, "mode") switch
                {
                    "first" => true,
                    "third" => false,
                    _ => throw new CommandException("expected first or third"),
                };
                return CommandResult.Success();
            },
        });
    }
}
