using Strata.Core.Common;
using Strata.Core.Content;

namespace Strata.Core.WorldGen.Structures.Dungeon;

/// <summary>What a blueprint leaves in a cell, as far as the blueprint alone can tell.</summary>
public enum ProbeCell : byte
{
    /// <summary>No op covers the cell: natural terrain (or a cave) of unknown solidity.</summary>
    Unknown,

    /// <summary>Carved to air.</summary>
    Air,

    /// <summary>A full solid block (brick, panel).</summary>
    Solid,

    /// <summary>A shaped solid block (platform, stair).</summary>
    Shaped,

    /// <summary>A non-solid block (cobweb).</summary>
    Soft,
}

/// <summary>
/// Answers "what will this cell be once the blueprint is stamped?" without stamping: the ops covering a cell are
/// scanned last to first and the first one that decides wins (a carve is air, a fill is its block, a shell's inside
/// and a replace leave the decision to earlier ops). A 16-cell coarse index keeps a query to the few ops near it.
/// Used by the dungeon decorator while the blueprint is still open (its own fills go through <see cref="Fill"/>).
/// </summary>
public sealed class BlueprintProbe
{
    private const int Shift = 4;
    private readonly List<BlueprintOp> _ops = new();
    private readonly Dictionary<long, List<int>> _index = new();
    private readonly Dictionary<string, bool> _solid = new(StringComparer.Ordinal);
    private readonly ContentDatabase _content;

    public BlueprintProbe(ContentDatabase content, Blueprint blueprint)
    {
        _content = content;
        foreach (var op in blueprint.Ops) Add(op);
    }

    /// <summary>Adds an op the decorator emits (it decides over every op added before it).</summary>
    public void Add(BlueprintOp op)
    {
        if (op is ObjectOp || op.Bounds.IsEmpty) return;
        int id = _ops.Count;
        _ops.Add(op);
        var b = op.Bounds;
        for (int y = b.Min.Y >> Shift; y <= (b.Max.Y - 1) >> Shift; y++)
            for (int z = b.Min.Z >> Shift; z <= (b.Max.Z - 1) >> Shift; z++)
                for (int x = b.Min.X >> Shift; x <= (b.Max.X - 1) >> Shift; x++)
                {
                    long key = Key(x, y, z);
                    if (!_index.TryGetValue(key, out var list)) _index[key] = list = new List<int>(4);
                    list.Add(id);
                }
    }

    public ProbeCell At(Int3 c)
    {
        if (!_index.TryGetValue(Key(c.X >> Shift, c.Y >> Shift, c.Z >> Shift), out var list)) return ProbeCell.Unknown;
        for (int i = list.Count - 1; i >= 0; i--)
        {
            var op = _ops[list[i]];
            if (!op.Bounds.Contains(c)) continue;
            switch (op)
            {
                case CarveOp:
                    return ProbeCell.Air;
                case FillOp f:
                    if (f.ShellOnly && f.Bounds.Expand(-1).Contains(c)) continue;
                    return !IsSolid(f.Block) ? ProbeCell.Soft : f.Shape != 0 ? ProbeCell.Shaped : ProbeCell.Solid;
                case CylinderOp cyl:
                {
                    float dx = c.X + 0.5f - cyl.CenterX, dz = c.Z + 0.5f - cyl.CenterZ;
                    if (dx * dx + dz * dz >= cyl.Radius * cyl.Radius) continue;
                    return cyl.Block == null ? ProbeCell.Air : IsSolid(cyl.Block) ? ProbeCell.Solid : ProbeCell.Soft;
                }
                case EllipsoidOp el:
                {
                    float dx = (c.X + 0.5f - el.Center.X) / el.Radii.X, dy = (c.Y + 0.5f - el.Center.Y) / el.Radii.Y, dz = (c.Z + 0.5f - el.Center.Z) / el.Radii.Z;
                    if (dx * dx + dy * dy + dz * dz >= 1f) continue;
                    return el.Block == null ? ProbeCell.Air : IsSolid(el.Block) ? ProbeCell.Solid : ProbeCell.Soft;
                }
                default:
                    continue; // replace ops keep air air and solid solid
            }
        }
        return ProbeCell.Unknown;
    }

    public bool IsAir(Int3 c) => At(c) == ProbeCell.Air;

    public bool IsSolid(Int3 c) => At(c) == ProbeCell.Solid;

    private bool IsSolid(string block)
    {
        if (!_solid.TryGetValue(block, out bool solid))
            _solid[block] = solid = _content.Blocks[_content.BlockId(block)].IsSolid;
        return solid;
    }

    private static long Key(int x, int y, int z) => ((long)(x + 0x100000) << 42) | ((long)(y + 0x100000) << 21) | (long)(z + 0x100000);
}
