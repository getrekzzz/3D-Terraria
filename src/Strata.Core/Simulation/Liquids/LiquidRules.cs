using Strata.Core.Common;
using Strata.Core.Content;
using Strata.Core.Gameplay.Interaction;
using Strata.Core.Voxels;

namespace Strata.Core.Simulation.Liquids;

/// <summary>Cell reads for the flow rules: an edit batch (with pending writes) or published chunk data.</summary>
internal interface ILiquidCells
{
    ushort Block(Int3 c);
    byte Liquid(Int3 c);
}

/// <summary>Reads through an edit batch, seeing this step's pending changes.</summary>
internal readonly struct BatchCells : ILiquidCells
{
    private readonly EditBatch _batch;

    public BatchCells(EditBatch batch) => _batch = batch;

    public ushort Block(Int3 c) => _batch.GetBlock(c);
    public byte Liquid(Int3 c) => _batch.GetLiquid(c);
}

/// <summary>Reads one chunk's published arrays directly, falling back to the world outside it.</summary>
internal readonly struct ChunkCells : ILiquidCells
{
    private readonly Int3 _origin;
    private readonly ChunkData _data;
    private readonly VoxelWorld _world;

    public ChunkCells(Int3 coord, ChunkData data, VoxelWorld world)
    {
        _origin = coord * ChunkMath.Size;
        _data = data;
        _world = world;
    }

    public ushort Block(Int3 c)
    {
        var l = c - _origin;
        return Inside(l) ? _data.GetBlock(ChunkMath.Index(l.X, l.Y, l.Z)) : _world.GetBlock(c);
    }

    public byte Liquid(Int3 c)
    {
        var l = c - _origin;
        return Inside(l) ? _data.Liquids?[ChunkMath.Index(l.X, l.Y, l.Z)] ?? 0 : _world.GetLiquid(c.X, c.Y, c.Z);
    }

    private static bool Inside(Int3 l) => (uint)l.X < ChunkMath.Size && (uint)l.Y < ChunkMath.Size && (uint)l.Z < ChunkMath.Size;
}

/// <summary>Shared questions the flow step and the wake scan ask about cells.</summary>
internal sealed class LiquidRules
{
    public static readonly Int3[] Horizontal = { new(1, 0, 0), new(-1, 0, 0), new(0, 0, 1), new(0, 0, -1) };

    private readonly VoxelWorld _world;
    private readonly ContentDatabase _content;

    public LiquidRules(VoxelWorld world, ContentDatabase content)
    {
        _world = world;
        _content = content;
    }

    public bool IsSolid(ushort block) => _content.Blocks[block].IsSolid;

    /// <summary>Can liquid occupy this cell? Unloaded, out-of-world, solid and closed-object cells cannot.</summary>
    public bool Open<T>(ref T cells, Int3 c) where T : struct, ILiquidCells
    {
        if (!_world.InBounds(c.X, c.Y, c.Z)) return false;
        ushort b = cells.Block(c);
        if (b == VoxelWorld.Unknown || IsSolid(b)) return false;
        var obj = _world.TileObjects.At(c);
        return obj == null || !ObjectInteraction.Blocks(obj);
    }

    /// <summary>Would a flow step change this liquid cell (fall, spread, react or evaporate)?</summary>
    public bool CouldFlow<T>(ref T cells, Int3 cell, byte liquid) where T : struct, ILiquidCells
    {
        var def = _content.LiquidOf(liquid);
        if (def == null) return false;
        int code = liquid >> 5, level = liquid & 0x1F;
        var below = cell + Int3.Down;
        if (Open(ref cells, below))
        {
            byte l = cells.Liquid(below);
            if (l == 0 || (l >> 5 == code && (l & 0x1F) < LiquidCode.MaxLevel)) return true;
        }
        foreach (var o in Faces.Offsets)
        {
            byte l = cells.Liquid(cell + o);
            if (l != 0 && l >> 5 != code && _content.MixOf(code, l >> 5) != 0) return true;
        }
        if (level < def.MinLevel && def.EvaporateChance > 0) return true;
        foreach (var o in Horizontal)
        {
            var n = cell + o;
            if (!Open(ref cells, n)) continue;
            byte l = cells.Liquid(n);
            if (l != 0 && l >> 5 != code) continue;
            int nl = l & 0x1F;
            if (level - nl >= def.SpreadThreshold && (level + nl) / 2 >= def.MinLevel) return true;
        }
        return false;
    }
}
