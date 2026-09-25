using System.Numerics;
using Strata.Core.Common;
using Strata.Core.Content.Defs;
using Strata.Core.Determinism;
using Strata.Core.Gameplay.Building;
using Strata.Core.Gameplay.Items;
using Strata.Core.Gameplay.Loot;
using Strata.Core.Gameplay.Mining;
using Strata.Core.Session;
using Strata.Core.Voxels;
using Strata.Core.Voxels.TileObjects;

namespace Strata.Core.Gameplay.Interaction;

/// <summary>Outcome of one mining swing.</summary>
public sealed class MineOutcome
{
    public List<Int3> Broken { get; } = new();

    /// <summary>Cells that took damage but did not break, with progress 0–1.</summary>
    public List<(Int3 Cell, float Progress)> Damaged { get; } = new();

    /// <summary>Cells the tool is too weak for (or protected).</summary>
    public List<Int3> TooHard { get; } = new();

    public List<TileObjectInstance> ObjectsBroken { get; } = new();

    public bool Any => Broken.Count > 0 || Damaged.Count > 0;
}

/// <summary>
/// Authoritative world modification for gameplay: mining with accumulated damage, block and tile-object
/// placement, object breaking with drops and loot, loose-block gravity and support loss. Every change is
/// committed through the session so lighting, sky exposure, saving and remeshing stay consistent.
/// </summary>
public sealed partial class WorldEditor
{
    private readonly WorldSession _session;
    private readonly DroppedItemSystem _drops;
    private readonly LootRoller _loot;
    private ulong _rngCounter;

    public WorldEditor(WorldSession session, DroppedItemSystem drops)
    {
        _session = session;
        _drops = drops;
        _loot = new LootRoller(session.Content);
    }

    public BlockDamageTracker Damage { get; } = new();

    /// <summary>Raised with every chunk whose mesh or lighting must be rebuilt.</summary>
    public event Action<HashSet<Int3>>? ChunksDirty;

    /// <summary>Raised when a cell is broken (for particles and sounds).</summary>
    public event Action<Int3, BlockDef>? BlockBroken;

    /// <summary>Raised when a block is placed.</summary>
    public event Action<Int3, BlockDef>? BlockPlaced;

    private VoxelWorld World => _session.World;

    /// <summary>
    /// Applies <paramref name="damage"/> to each cell of a brush with a tool of <paramref name="pickPower"/>.
    /// Broken cells drop their item; loose blocks above fall; unsupported objects break.
    /// </summary>
    public MineOutcome Mine(IEnumerable<Int3> cells, int pickPower, float damage, long tick)
    {
        var outcome = new MineOutcome();
        var content = _session.Content;
        var batch = World.BeginEdit();
        foreach (var cell in cells)
        {
            ushort id = World.GetBlock(cell);
            if (id == 0 || id == VoxelWorld.Unknown) continue;
            var block = content.Blocks[id];
            if (!MiningRules.CanMine(block, pickPower))
            {
                outcome.TooHard.Add(cell);
                continue;
            }
            float health = MiningRules.Health(block);
            float total = Damage.Hit(cell, damage, tick);
            if (total < health)
            {
                outcome.Damaged.Add((cell, total / health));
                continue;
            }
            Damage.Clear(cell);
            batch.SetBlock(cell, 0);
            outcome.Broken.Add(cell);
            if (content.BlockDrops[id] is { } drop) SpawnDrop(new ItemStack(drop, 1), cell);
            BlockBroken?.Invoke(cell, block);
        }
        if (outcome.Broken.Count == 0) return outcome;
        FallingBlocks.Settle(batch, content, outcome.Broken);
        Commit(batch);
        BreakUnsupportedAround(outcome.Broken, outcome.ObjectsBroken);
        return outcome;
    }

    /// <summary>Places a block (crisp shapes carry rotation). Does not consume items; the caller does.</summary>
    public PlacementResult PlaceBlock(Int3 cell, BlockDef block, byte shape, IEnumerable<(Vector3 Min, Vector3 Max)> blockers)
    {
        var result = PlacementRules.CanPlaceBlock(World, cell, block, blockers);
        if (result != PlacementResult.Ok) return result;
        var decor = World.TileObjects.At(cell);
        if (decor != null) BreakObject(decor, dropItems: true);
        var batch = World.BeginEdit();
        batch.SetBlock(cell, (ushort)block.RuntimeId, shape, CellFlags.PlayerPlaced);
        var opened = new List<Int3>();
        if (block.Falls && World.GetBlock(cell + Int3.Down) == 0) opened.Add(cell);
        FallingBlocks.Settle(batch, _session.Content, opened);
        Commit(batch);
        BlockPlaced?.Invoke(cell, block);
        return PlacementResult.Ok;
    }

    /// <summary>Removes a block without drops (hammer reshaping, commands).</summary>
    public void SetBlock(Int3 cell, ushort block, byte shape = 0, CellFlags flags = CellFlags.None)
    {
        var batch = World.BeginEdit();
        batch.SetBlock(cell, block, shape, flags);
        Commit(batch);
        if (block == 0) BreakUnsupportedAround(new[] { cell }, new List<TileObjectInstance>());
    }

    /// <summary>Changes the shape of a player-placed crisp block (hammer).</summary>
    public bool Reshape(Int3 cell, byte shape)
    {
        ushort id = World.GetBlock(cell);
        if (id == 0 || id == VoxelWorld.Unknown || _session.Content.Blocks[id].Render != BlockRender.Crisp) return false;
        var batch = World.BeginEdit();
        batch.SetBlock(cell, id, shape);
        Commit(batch);
        return true;
    }

    private void Commit(EditBatch batch)
    {
        var dirty = _session.Commit(batch);
        if (dirty.Count > 0) ChunksDirty?.Invoke(dirty);
    }

    private Pcg32 NextRng(Int3 cell) =>
        new(Hash.Combine(Hash.Cell((ulong)_session.Settings.Seed, cell.X, cell.Y, cell.Z), (long)(_session.Clock.Tick * 131 + (long)++_rngCounter)));

    private void SpawnDrop(ItemStack stack, Int3 cell)
    {
        var rng = NextRng(cell);
        var pos = (new Vector3(cell.X, cell.Y, cell.Z) + new Vector3(0.5f)) * Units.CellSize;
        var vel = new Vector3(rng.NextSigned() * 1.2f, 2.5f + rng.NextFloat() * 1.5f, rng.NextSigned() * 1.2f);
        _drops.Spawn(stack, pos, vel);
    }
}
