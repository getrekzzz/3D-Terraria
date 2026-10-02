using Strata.Core.Gameplay.Crafting;
using Strata.Core.Gameplay.Guide;

namespace Strata.Core.Commands;

/// <summary>Journal and crafting commands (tests and smoke scripts).</summary>
public static class GuideCommands
{
    public static void Register(CommandRegistry r)
    {
        r.Register(new CommandDef
        {
            Name = "journal", Usage = "journal [reset | unlock <enemy> [kills]]",
            Help = "Shows the player's journal totals, forgets everything, or records kills of a creature.",
            Run = (ctx, a) =>
            {
                var j = ctx.Player.State.Journal;
                switch (a.OptStr(0))
                {
                    case null:
                        return CommandResult.Success($"bestiary {j.BestiaryPercent:0.0}%, {j.KillCounts.Count} kinds killed, {j.Seen.Count} seen, " +
                            $"{j.Met.Count} met, {j.Obtained.Count} items obtained, {j.Crafted.Count} crafted, {j.Achievements.Count} achievements");
                    case "reset":
                        j.Clear();
                        return CommandResult.Success("Journal cleared.");
                    case "unlock":
                        var id = a.Str(1, "enemy");
                        if (!ctx.World.Session.Content.Enemies.TryGet(id, out var def)) throw new CommandException($"unknown enemy '{id}'");
                        j.MarkSeen(Bestiary.EntryOf(def));
                        for (int i = a.OptInt(2, 1); i > 0; i--) j.AddKill(def.Id);
                        return CommandResult.Success($"{def.DisplayName}: {j.Kills(def.Id)} kills.");
                    default:
                        throw new CommandException("usage: journal [reset | unlock <enemy> [kills]]");
                }
            },
        });
        r.Register(new CommandDef
        {
            Name = "craft", Usage = "craft <item> [times]",
            Help = "Crafts an item from the backpack with the stations around the player, as the crafting menu does.",
            Run = (ctx, a) =>
            {
                var id = a.Str(0, "item");
                if (!ctx.World.Session.Content.Items.TryGet(id, out var item)) throw new CommandException($"unknown item '{id}'");
                var env = CraftingEnvironment.Around(ctx.World, ctx.Player);
                foreach (var recipe in ctx.World.Recipes.RecipesFor(item))
                {
                    int crafted = ctx.World.Craft(ctx.Player, recipe, env, a.OptInt(1, 1));
                    if (crafted > 0) return CommandResult.Success($"Crafted {crafted * recipe.Def.Count} {item.DisplayName}.");
                }
                return CommandResult.Fail($"Cannot craft {item.DisplayName} here (ingredients, stations or room).");
            },
        });
    }
}
