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
}
