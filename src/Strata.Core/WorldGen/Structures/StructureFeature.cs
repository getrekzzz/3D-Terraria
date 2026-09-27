using Strata.Core.WorldGen.Features;

namespace Strata.Core.WorldGen.Structures;

/// <summary>A planned structure (cabin, dungeon, pyramid…) described by a blueprint.</summary>
public sealed class StructureFeature : WorldFeature
{
    public StructureFeature(Blueprint blueprint)
    {
        Blueprint = blueprint;
        Bounds = blueprint.Bounds;
    }

    public Blueprint Blueprint { get; }

    /// <summary>Inside one of the blueprint's solid or carved volumes (not just the overall box: a sprawling crypt's
    /// bounding box would claim the open surface above it).</summary>
    public override bool InZone(Common.Int3 cell) =>
        Bounds.Contains(cell) && Blueprint.Ops.Any(op => op is not ObjectOp && op.Bounds.Contains(cell));
}
