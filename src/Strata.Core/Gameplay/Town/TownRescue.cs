namespace Strata.Core.Gameplay.Town;

/// <summary>Rescuable town NPCs (found bound in the world): the world flag set when one is freed.</summary>
public static class TownRescue
{
    public const string FlagPrefix = "rescued_";

    public static string FlagFor(string id) => FlagPrefix + id;
}
