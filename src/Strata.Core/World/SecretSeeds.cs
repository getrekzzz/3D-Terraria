namespace Strata.Core.World;

/// <summary>Terraria's special world seeds, typed into the seed field instead of a number (X-07).</summary>
[Flags]
public enum SecretSeed
{
    None = 0,

    /// <summary>For the Worthy: every difficulty plays one step harder (Classic as Expert, Expert as Master).</summary>
    ForTheWorthy = 1,

    /// <summary>Drunk World: both evils (Corruption and Crimson) generate side by side, with both kinds of altar.</summary>
    Drunk = 2,

    /// <summary>Not the Bees: a far larger jungle riddled with hives.</summary>
    NotTheBees = 4,

    /// <summary>Don't Dig Up: an upside-down world — the underworld near the top (where the player starts), the surface at the bottom.</summary>
    DontDigUp = 8,

    /// <summary>The Constant: a dim world with darker nights.</summary>
    TheConstant = 16,

    /// <summary>Celebration: the Party never really ends (it can start without the Party Girl).</summary>
    Celebration = 32,

    /// <summary>No Traps: the Dungeon's halls bristle with dart traps.</summary>
    NoTraps = 64,

    /// <summary>Get Fixed Boi: all of the above.</summary>
    GetFixedBoi = ForTheWorthy | Drunk | NotTheBees | DontDigUp | TheConstant | Celebration | NoTraps,
}

/// <summary>Recognises secret seed names (any case, spaces and punctuation ignored) and names saved flags.</summary>
public static class SecretSeeds
{
    private static readonly (SecretSeed Seed, string Id, string Title, string[] Names)[] Table =
    {
        (SecretSeed.ForTheWorthy, "for_the_worthy", "For the Worthy", new[] { "fortheworthy" }),
        (SecretSeed.Drunk, "drunk_world", "Drunk World", new[] { "drunkworld", "05162020", "5162020" }),
        (SecretSeed.NotTheBees, "not_the_bees", "Not the Bees", new[] { "notthebees" }),
        (SecretSeed.DontDigUp, "dont_dig_up", "Don't Dig Up", new[] { "dontdigup" }),
        (SecretSeed.TheConstant, "the_constant", "The Constant", new[] { "theconstant", "constant", "eye4aneye" }),
        (SecretSeed.Celebration, "celebration", "Celebration", new[] { "celebrationmk10", "05162011", "5162011", "05162021", "5162021" }),
        (SecretSeed.NoTraps, "no_traps", "No Traps", new[] { "notraps" }),
    };

    /// <summary>Letters and digits only, lower case ("Don't Dig Up!" → "dontdigup").</summary>
    public static string Normalize(string text) =>
        new(text.Where(char.IsLetterOrDigit).Select(char.ToLowerInvariant).ToArray());

    /// <summary>The secret seed a typed seed names, or <see cref="SecretSeed.None"/>.</summary>
    public static SecretSeed Parse(string text)
    {
        string n = Normalize(text ?? "");
        if (n.Length == 0) return SecretSeed.None;
        if (n == "getfixedboi") return SecretSeed.GetFixedBoi;
        foreach (var (seed, _, _, names) in Table)
            if (names.Contains(n)) return seed;
        return SecretSeed.None;
    }

    /// <summary>Saved ids of the flags set ("for_the_worthy", …), in table order.</summary>
    public static List<string> Ids(SecretSeed flags) => Table.Where(t => (flags & t.Seed) != 0).Select(t => t.Id).ToList();

    /// <summary>Display titles of the flags set.</summary>
    public static List<string> Titles(SecretSeed flags) =>
        flags == SecretSeed.GetFixedBoi ? new List<string> { "Get Fixed Boi" } : Table.Where(t => (flags & t.Seed) != 0).Select(t => t.Title).ToList();

    /// <summary>Flags from saved ids (unknown ids are ignored).</summary>
    public static SecretSeed FromIds(IEnumerable<string>? ids)
    {
        var flags = SecretSeed.None;
        foreach (var id in ids ?? Array.Empty<string>())
            foreach (var t in Table)
                if (t.Id == id) flags |= t.Seed;
        return flags;
    }

    /// <summary>One flag by id ("celebration"), or None.</summary>
    public static SecretSeed FromId(string id) => FromIds(new[] { id });
}
