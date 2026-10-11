using Strata.Core.Common;
using Strata.Core.Content;
using Strata.Core.Persistence;
using Strata.Core.Voxels;

namespace Strata.Core.Gameplay.Wiring;

/// <summary>The four wire colours; each is its own network (a cell may carry several).</summary>
[Flags]
public enum WireColor : byte
{
    None = 0,
    Red = 1,
    Blue = 2,
    Green = 4,
    Yellow = 8,
}

public static class WireColors
{
    public static readonly WireColor[] All = { WireColor.Red, WireColor.Blue, WireColor.Green, WireColor.Yellow };

    public static WireColor Parse(string name) => name.ToLowerInvariant() switch
    {
        "red" => WireColor.Red,
        "blue" => WireColor.Blue,
        "green" => WireColor.Green,
        "yellow" => WireColor.Yellow,
        _ => throw new ArgumentException($"unknown wire colour '{name}' (red, blue, green, yellow)"),
    };

    /// <summary>Index 0–3 of a single colour.</summary>
    public static int Index(WireColor c) => c switch { WireColor.Red => 0, WireColor.Blue => 1, WireColor.Green => 2, _ => 3 };
}

/// <summary>A block switched off by its actuator: it is air in the voxel grid until switched back on.</summary>
public readonly record struct InactiveBlock(ushort Block, byte Shape, byte Flags);

/// <summary>
/// Wiring laid through cells: a bit per wire colour plus an actuator bit, and the blocks actuators have switched off.
/// Saved in world.json (red wire keeps its original field, so older saves load unchanged).
/// </summary>
public sealed class WireGrid
{
    public const byte ActuatorBit = 16;
    private const byte WireBits = 15;

    private readonly Dictionary<Int3, byte> _cells = new();

    /// <summary>Wired or actuated cells and their bits.</summary>
    public IReadOnlyDictionary<Int3, byte> Cells => _cells;

    /// <summary>The wired or actuated cells.</summary>
    public IReadOnlyCollection<Int3> Keys => _cells.Keys;

    /// <summary>Blocks currently switched off by actuators, by cell.</summary>
    public Dictionary<Int3, InactiveBlock> Inactive { get; } = new();

    /// <summary>Cells carrying at least one wire.</summary>
    public int WireCount { get; private set; }

    public byte MaskAt(Int3 cell) => _cells.TryGetValue(cell, out var m) ? m : (byte)0;
    public bool Has(Int3 cell, WireColor color) => (MaskAt(cell) & (byte)color) != 0;
    public bool HasAnyWire(Int3 cell) => (MaskAt(cell) & WireBits) != 0;
    public bool HasActuator(Int3 cell) => (MaskAt(cell) & ActuatorBit) != 0;

    public bool Add(Int3 cell, WireColor color) => Set(cell, (byte)color, true);
    public bool Remove(Int3 cell, WireColor color) => Set(cell, (byte)color, false);
    public bool SetActuator(Int3 cell, bool on) => Set(cell, ActuatorBit, on);

    private bool Set(Int3 cell, byte bit, bool on)
    {
        byte old = MaskAt(cell);
        byte now = on ? (byte)(old | bit) : (byte)(old & ~bit);
        if (now == old) return false;
        if ((old & WireBits) == 0 && (now & WireBits) != 0) WireCount++;
        else if ((old & WireBits) != 0 && (now & WireBits) == 0) WireCount--;
        if (now == 0) _cells.Remove(cell);
        else _cells[cell] = now;
        return true;
    }

    public void Load(WorldMeta meta, ContentDatabase content)
    {
        AddTriples(meta.Wires, WireColor.Red);
        AddTriples(meta.WiresBlue, WireColor.Blue);
        AddTriples(meta.WiresGreen, WireColor.Green);
        AddTriples(meta.WiresYellow, WireColor.Yellow);
        var act = meta.Actuators ?? new();
        for (int i = 0; i + 2 < act.Count; i += 3) SetActuator(new Int3(act[i], act[i + 1], act[i + 2]), true);
        foreach (var b in meta.InactiveBlocks ?? new())
            if (content.Blocks.TryGet(b.Block, out var def))
                Inactive[new Int3(b.X, b.Y, b.Z)] = new InactiveBlock((ushort)def.RuntimeId, b.Shape, b.Flags);
    }

    private void AddTriples(List<int>? list, WireColor color)
    {
        if (list == null) return;
        for (int i = 0; i + 2 < list.Count; i += 3) Add(new Int3(list[i], list[i + 1], list[i + 2]), color);
    }

    public void Save(WorldMeta meta, ContentDatabase content)
    {
        var ordered = _cells.OrderBy(c => c.Key.Y).ThenBy(c => c.Key.Z).ThenBy(c => c.Key.X).ToList();
        List<int> Triples(byte bit) => ordered.Where(c => (c.Value & bit) != 0).SelectMany(c => new[] { c.Key.X, c.Key.Y, c.Key.Z }).ToList();
        meta.Wires = Triples((byte)WireColor.Red);
        meta.WiresBlue = Triples((byte)WireColor.Blue);
        meta.WiresGreen = Triples((byte)WireColor.Green);
        meta.WiresYellow = Triples((byte)WireColor.Yellow);
        meta.Actuators = Triples(ActuatorBit);
        meta.InactiveBlocks = Inactive.OrderBy(c => c.Key.Y).ThenBy(c => c.Key.Z).ThenBy(c => c.Key.X)
            .Select(c => new WorldMeta.InactiveBlockModel(c.Key.X, c.Key.Y, c.Key.Z, content.Blocks[c.Value.Block].Id, c.Value.Shape, c.Value.Flags)).ToList();
    }
}
