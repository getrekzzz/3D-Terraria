using Strata.Core.Common;
using Strata.Core.Content;
using Strata.Core.Content.Defs;
using Strata.Core.Determinism;
using Strata.Core.Session;
using Strata.Core.Voxels;

namespace Strata.Core.Simulation.Liquids;

/// <summary>
/// Cellular liquid flow over the voxel world. Only <em>active</em> cells are simulated: cells woken by edits,
/// by chunks loading next to them, or by their own flow. Each liquid steps at its own interval: cells fall
/// first, then share sideways with lower neighbours, react with other liquids (water + lava → obsidian) and
/// thin remnants evaporate. Every step is one edit batch, so saving, lighting and remeshing follow as for any
/// other edit. Quiet water (oceans, settled lakes) costs nothing.
/// </summary>
public sealed partial class LiquidSimulator
{
    /// <summary>Upper bound on cells evaluated per liquid per step; the rest carry over.</summary>
    public const int MaxCellsPerStep = 3000;

    /// <summary>Levels a liquid spends when it hardens another liquid's cell.</summary>
    public const int ReactionCost = 8;

    private readonly WorldSession _session;
    private readonly ContentDatabase _content;
    private readonly LiquidRules _rules;
    private readonly HashSet<Int3>[] _active;
    private readonly List<Int3> _order = new();
    private readonly HashSet<Int3> _received = new();
    private readonly HashSet<Int3> _changed = new();
    private readonly List<Int3> _keep = new();
    private bool _committing;

    public LiquidSimulator(WorldSession session)
    {
        _session = session;
        _content = session.Content;
        _rules = new LiquidRules(session.World, _content);
        _active = new HashSet<Int3>[LiquidDef.MaxCode + 1];
        for (int i = 0; i < _active.Length; i++) _active[i] = new HashSet<Int3>();
        session.World.Edited += OnEdited;
        session.ChunkInstalled += OnChunkInstalled;
    }

    /// <summary>Pauses all flow (debug command); woken cells are kept.</summary>
    public bool Enabled { get; set; } = true;

    public int ActiveCells => _active.Sum(s => s.Count);

    public long Steps { get; private set; }

    /// <summary>Cells changed by the most recent step.</summary>
    public int LastChanged { get; private set; }

    /// <summary>Chunks whose meshes or light must be rebuilt after a step.</summary>
    public event Action<HashSet<Int3>>? ChunksDirty;

    /// <summary>Two liquids met and formed a block (hiss, steam).</summary>
    public event Action<Int3, BlockDef>? Mixed;

    private VoxelWorld World => _session.World;

    /// <summary>One simulation tick: each liquid whose interval divides the tick takes a flow step.</summary>
    public void Tick(long tick)
    {
        if (!Enabled) return;
        for (int code = 1; code <= LiquidDef.MaxCode; code++)
        {
            var def = _content.LiquidByCode[code];
            if (def == null || _active[code].Count == 0 || tick % def.FlowInterval != 0) continue;
            Step(def);
        }
    }

    /// <summary>Steps every liquid until nothing is active or <paramref name="maxSteps"/> rounds ran. Returns rounds.</summary>
    public int Settle(int maxSteps)
    {
        int rounds = 0;
        while (rounds < maxSteps && ActiveCells > 0)
        {
            for (int code = 1; code <= LiquidDef.MaxCode; code++)
                if (_content.LiquidByCode[code] is { } def && _active[code].Count > 0) Step(def);
            rounds++;
        }
        return rounds;
    }

    /// <summary>Marks a liquid cell for evaluation on its liquid's next step.</summary>
    public void Wake(Int3 cell)
    {
        byte l = World.GetLiquid(cell.X, cell.Y, cell.Z);
        if (l != 0 && _content.LiquidByCode[l >> 5] != null) _active[l >> 5].Add(cell);
    }

    /// <summary>Wakes a cell and its six neighbours.</summary>
    public void WakeAround(Int3 cell)
    {
        Wake(cell);
        foreach (var o in Faces.Offsets) Wake(cell + o);
    }

    public bool IsActive(Int3 cell) => _active.Any(s => s.Contains(cell));

    private void OnEdited(EditResult result)
    {
        if (_committing) return;
        foreach (var c in result.Changes) WakeAround(c.Cell);
        foreach (var c in result.LiquidChanges) WakeAround(c.Cell);
    }

    private void Step(LiquidDef def)
    {
        var set = _active[def.Code];
        _order.Clear();
        _order.AddRange(set);
        set.Clear();
        _order.Sort(static (a, b) => a.Y != b.Y ? a.Y.CompareTo(b.Y) : a.Z != b.Z ? a.Z.CompareTo(b.Z) : a.X.CompareTo(b.X));
        int count = Math.Min(_order.Count, MaxCellsPerStep);
        for (int i = count; i < _order.Count; i++) set.Add(_order[i]);

        _received.Clear();
        _changed.Clear();
        _keep.Clear();
        var batch = World.BeginEdit();
        for (int i = 0; i < count; i++)
        {
            var cell = _order[i];
            if (_received.Contains(cell)) _keep.Add(cell); // filled this step: moves next step
            else FlowCell(batch, def, cell);
        }
        Steps++;
        LastChanged = _changed.Count;
        if (_changed.Count > 0)
        {
            HashSet<Int3> dirty;
            _committing = true;
            try
            {
                dirty = _session.Commit(batch);
            }
            finally
            {
                _committing = false;
            }
            foreach (var c in _changed) WakeAround(c);
            ChunksDirty?.Invoke(dirty);
        }
        foreach (var c in _keep) Wake(c);
    }

    private float Roll(Int3 cell) =>
        Hash.ToUnitFloat(Hash.Combine(Hash.Cell((ulong)_session.Settings.Seed ^ 0x51D0u, cell.X, cell.Y, cell.Z), Steps));
}
