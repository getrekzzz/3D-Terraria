using Strata.Core.Gameplay.Town;

namespace Strata.Core.Commands;

/// <summary>
/// The Stylist's services as <c>town</c> subcommands (the conversation buttons arrive with the W4
/// dialog). Returns null for an unknown subcommand.
/// </summary>
internal static class TownServiceCommands
{
    public const string Usage = "haircut <id> <style> <#rrggbb>";

    public static CommandResult? TryRun(CommandContext ctx, CommandArgs a)
    {
        var services = ctx.World.Services;
        switch (a.OptStr(0))
        {
            case "haircut":
                var stylist = TownCommands.Resident(ctx, a);
                string style = a.Str(2, "style"), color = a.Str(3, "#rrggbb");
                long cost = services.HaircutCost(stylist, ctx.Player, style, color);
                var cut = services.Haircut(stylist, ctx.Player, style, color);
                return cut == TradeResult.Ok ? CommandResult.Success($"New hair: {style} {color} for {cost} copper.") : CommandResult.Fail(cut.ToString());
            default:
                return null;
        }
    }
}
