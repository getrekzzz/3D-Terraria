using Strata.Core.Determinism;

namespace Strata.Core.WorldGen.Structures.Dungeon;

/// <summary>The dungeon's colour, as in Terraria one of blue, green or pink: its brick (shells, floors, ceilings,
/// fixtures) and the three matching wall panels a hall or room lines its sides with (indexed by PanelStyle).</summary>
public sealed record DungeonStyle(string Colour, string Brick, IReadOnlyList<string> Panels)
{
    private static readonly string[] Colours = { "blue", "green", "pink" };

    public static DungeonStyle Of(string colour) =>
        new(colour, colour + "_brick", new[] { colour + "_brick_wall", colour + "_slab_wall", colour + "_tiled_wall" });

    public static DungeonStyle Pick(ref Pcg32 rng) => Of(Colours[rng.NextInt(Colours.Length)]);

    /// <summary>The panel block for a hall's or room's PanelStyle (0..2).</summary>
    public string Panel(int style) => Panels[((style % Panels.Count) + Panels.Count) % Panels.Count];
}
