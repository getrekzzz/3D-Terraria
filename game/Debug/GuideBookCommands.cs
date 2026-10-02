using Strata.Core.Commands;
using Strata.Game.Bootstrap;
using Strata.Game.UI.GuideBook;

namespace Strata.Game.Debug;

/// <summary>The <c>book</c> command: open the Guide book at a tab or entry, or close it (smoke scripts, screenshots).</summary>
public static class GuideBookCommands
{
    private const string Usage = "book <items|bestiary|progress|npcs|tips|achievements|item <id>|bestiary <id>|entry <id>|close>";

    public static GuideBookScreen? Find(LaunchedWorld world) => world.Runtime.GetNodeOrNull<GuideBookScreen>(GuideBookScreen.NodeName);

    public static void Register(CommandRegistry r, LaunchedWorld world)
    {
        r.Register(new CommandDef
        {
            Name = "book", Usage = Usage,
            Help = "Opens the Guide book at a tab, an item, a bestiary entry or any entry (item, creature, resident, guide step or tip), or closes it.",
            Run = (_, a) =>
            {
                var book = Find(world) ?? throw new CommandException("no Guide book in this mode");
                string what = a.Str(0, "tab");
                string? id = a.OptStr(1);
                switch (what)
                {
                    case "close":
                        book.Close();
                        return CommandResult.Success("Closed.");
                    case "item" or "entry" or "bestiary" when id != null:
                        if (!book.IndexReady && what != "bestiary") return CommandResult.Fail("The book is still loading.");
                        bool ok = what switch
                        {
                            "item" => book.Open("items") && book.Items.Show(id),
                            "bestiary" => book.Open("bestiary") && book.Bestiary.Show(id),
                            _ => book.OpenEntry(id),
                        };
                        return ok ? CommandResult.Success($"Showing {id}.") : CommandResult.Fail($"No entry '{id}'.");
                    default:
                        return book.Open(what) ? CommandResult.Success($"Book open at {what}.") : throw new CommandException("usage: " + Usage);
                }
            },
        });
    }
}
