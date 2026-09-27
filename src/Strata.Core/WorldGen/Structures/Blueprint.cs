using Strata.Core.Common;
using Strata.Core.Voxels;

namespace Strata.Core.WorldGen.Structures;

/// <summary>One primitive operation of a structure blueprint, applied in order.</summary>
public abstract record BlueprintOp(Aabb3i Bounds);

/// <summary>Fills a box with a block (optionally only replacing air, or only its shell).</summary>
public sealed record FillOp(Aabb3i Bounds, string Block, byte Shape = 0, bool OnlyAir = false, bool ShellOnly = false) : BlueprintOp(Bounds);

/// <summary>Carves a box to air (optionally filling with a liquid).</summary>
public sealed record CarveOp(Aabb3i Bounds, byte Liquid = 0) : BlueprintOp(Bounds);

/// <summary>Replaces natural (non-air) cells in a box with a block — e.g. evil conversion, lining.</summary>
public sealed record ReplaceOp(Aabb3i Bounds, string Block, string[]? OnlyReplace = null) : BlueprintOp(Bounds);

/// <summary>Fills (or carves, when <see cref="Block"/> is null) an axis-aligned ellipsoid.</summary>
public sealed record EllipsoidOp(Aabb3i Bounds, System.Numerics.Vector3 Center, System.Numerics.Vector3 Radii, string? Block, bool OnlyAir = false, byte Liquid = 0) : BlueprintOp(Bounds);

/// <summary>Fills (or carves, when <see cref="Block"/> is null) a vertical cylinder.</summary>
public sealed record CylinderOp(Aabb3i Bounds, float CenterX, float CenterZ, float Radius, string? Block, bool OnlyAir = false) : BlueprintOp(Bounds);

/// <summary>Places a tile object anchored at <see cref="Anchor"/>.</summary>
public sealed record ObjectOp(Aabb3i Bounds, string ObjectId, Int3 Anchor, int Rotation, int Variant = 0, string? Loot = null) : BlueprintOp(Bounds);

/// <summary>A deterministic list of operations describing a structure, produced during planning.</summary>
public sealed class Blueprint
{
    private readonly List<BlueprintOp> _ops = new();
    private Aabb3i _bounds = Aabb3i.Empty;

    public IReadOnlyList<BlueprintOp> Ops => _ops;
    public Aabb3i Bounds => _bounds;

    /// <summary>Cells written by this structure are flagged (e.g. protected dungeon walls).</summary>
    public CellFlags Flags { get; init; } = CellFlags.Structure;

    public void Fill(Aabb3i box, string block, byte shape = 0, bool onlyAir = false) => Add(new FillOp(box, block, shape, onlyAir));

    public void Shell(Aabb3i box, string block) => Add(new FillOp(box, block, 0, false, true));

    public void Carve(Aabb3i box, byte liquid = 0) => Add(new CarveOp(box, liquid));

    public void Replace(Aabb3i box, string block, params string[] only) => Add(new ReplaceOp(box, block, only.Length == 0 ? null : only));

    /// <summary>Hollow room: shell of <paramref name="wall"/> with an air interior.</summary>
    public void Room(Aabb3i box, string wall)
    {
        Fill(box, wall);
        Carve(box.Expand(-1));
    }

    public void Ellipsoid(System.Numerics.Vector3 center, System.Numerics.Vector3 radii, string? block, bool onlyAir = false, byte liquid = 0)
    {
        var min = new Int3((int)MathF.Floor(center.X - radii.X), (int)MathF.Floor(center.Y - radii.Y), (int)MathF.Floor(center.Z - radii.Z));
        var max = new Int3((int)MathF.Ceiling(center.X + radii.X) + 1, (int)MathF.Ceiling(center.Y + radii.Y) + 1, (int)MathF.Ceiling(center.Z + radii.Z) + 1);
        Add(new EllipsoidOp(new Aabb3i(min, max), center, radii, block, onlyAir, liquid));
    }

    public void Cylinder(float cx, float cz, float radius, int yMin, int yMax, string? block, bool onlyAir = false)
    {
        int r = (int)MathF.Ceiling(radius) + 1;
        Add(new CylinderOp(new Aabb3i(new Int3((int)cx - r, yMin, (int)cz - r), new Int3((int)cx + r + 1, yMax, (int)cz + r + 1)), cx, cz, radius, block, onlyAir));
    }

    public void Object(string id, Int3 anchor, Int3 footprint, int rotation = 0, int variant = 0, string? loot = null) =>
        Add(new ObjectOp(new Aabb3i(anchor, anchor + footprint), id, anchor, rotation, variant, loot));

    /// <summary>Places a tile object, computing its rotated footprint from the definition.</summary>
    public void Object(Content.Defs.TileObjectDef def, Int3 anchor, int rotation = 0, int variant = 0, string? loot = null) =>
        Object(def.Id, anchor, Voxels.TileObjects.TileObjectInstance.FootprintOf(def, rotation), rotation, variant, loot);

    public void Add(BlueprintOp op)
    {
        _ops.Add(op);
        _bounds = _bounds.Union(op.Bounds);
    }
}
