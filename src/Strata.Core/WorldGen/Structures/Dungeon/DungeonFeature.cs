namespace Strata.Core.WorldGen.Structures.Dungeon;

/// <summary>The planned dungeon in the world plan: its carved blueprint plus the layout and style it was built from,
/// so later passes, the decorator and tests can find halls, rooms and the keep without magic offsets.</summary>
public sealed class DungeonFeature : StructureFeature
{
    public DungeonFeature(Blueprint blueprint, DungeonLayout layout, DungeonStyle style) : base(blueprint)
    {
        Layout = layout;
        Style = style;
    }

    public DungeonLayout Layout { get; }
    public DungeonStyle Style { get; }
}
