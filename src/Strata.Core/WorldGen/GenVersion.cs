namespace Strata.Core.WorldGen;

/// <summary>
/// Bump whenever generator output changes for the same seed. Stored in saves; golden-hash tests pin it.
/// </summary>
public static class GenVersion
{
    public const int Current = 5; // 5: Jungle Temple, Plantera's Bulbs; 4: shadow orbs/crimson hearts, demon altars, bee larvae; 3: living trees can be climbed out of (staggered platforms, wider door); thinner forests
}
