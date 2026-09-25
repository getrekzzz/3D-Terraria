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
            Name = "ui", Usage = "ui <inventory|close>", Help = "Opens or closes gameplay screens.",
            Run = (ctx, a) =>
            {
                var screen = world.Inventory ?? throw new CommandException("no inventory screen in this mode");
                switch (a.Str(0, "screen"))
                {
                    case "inventory": screen.SetOpen(true); return CommandResult.Success("Inventory open.");
                    case "close": screen.SetOpen(false); return CommandResult.Success("Closed.");
                    default: throw new CommandException("expected inventory or close");
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
