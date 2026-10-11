namespace Strata.Core.WorldGen.Structures.Dungeon;

/// <summary>The planned dungeon in the world plan: its carved blueprint plus the layout and style it was built from,
/// so later passes, the decorator and tests can find halls, rooms and the keep without magic offsets.</summary>
public sealed class DungeonFeature : StructureFeature
{
    public DungeonFeature(Blueprint blueprint, DungeonLayout layout, DungeonStyle style) : base(blueprint)
    {
        Layout = layout;
        Style = style;
        var ids = new SortedSet<string>(StringComparer.Ordinal);
        foreach (var op in blueprint.Ops) if (op is ObjectOp o) ids.Add(o.ObjectId);
        ObjectIds = ids.ToArray();
    }

    /// <summary>Every tile object id the dungeon places (sorted), so the renderer can preload their models.</summary>
    public IReadOnlyList<string> ObjectIds { get; }

    public DungeonLayout Layout { get; }
    public DungeonStyle Style { get; }
}
