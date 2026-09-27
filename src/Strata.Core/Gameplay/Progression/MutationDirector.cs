using Strata.Core.Common;
using Strata.Core.Content.Defs;
using Strata.Core.Session;
using Strata.Core.Voxels;
using Strata.Core.WorldGen.Mutations;

namespace Strata.Core.Gameplay.Progression;

/// <summary>
/// Turns progression into world change: when a flag is raised, the mutations it triggers are appended to the world's
/// mutation log. Chunks produced from then on arrive already mutated; chunks already loaded (or produced during the
/// append) are caught up here a few per tick, as ordinary edits so lighting, meshes and saves follow.
/// </summary>
public sealed class MutationDirector
{
    /// <summary>Loaded chunks brought up to date per tick (spreads the hardmode transition over a few seconds).</summary>
    public const int CatchUpPerTick = 12;

    private readonly GameWorld _world;

    public MutationDirector(GameWorld world)
    {
        _world = world;
        world.Progression.Raised += Raised;
    }

    private MutationLog Log => _world.Session.Mutations;

    /// <summary>A mutation joined the log (announcements, tests).</summary>
    public event Action<WorldMutationDef>? Mutated;

    /// <summary>Loaded chunks still behind the mutation level.</summary>
    public int Behind => _world.Session.World.Chunks.Count(c => c.MutationLevel < Log.Level);

    private void Raised(ProgressionFlagDef flag)
    {
        foreach (var def in _world.Session.Content.Mutations.All)
        {
            if (def.Trigger != flag.Id || Log.Contains(def.Id)) continue;
            Log.Append(def);
            Mutated?.Invoke(def);
        }
    }

    public void Tick()
    {
        int level = Log.Level;
        if (level == 0) return;
        int budget = CatchUpPerTick;
        foreach (var chunk in _world.Session.World.Chunks.Where(c => c.MutationLevel < level).Take(budget).ToList())
            CatchUp(chunk);
    }

    /// <summary>Brings every loaded chunk up to the mutation level now (commands, tests).</summary>
    public int CatchUpAll()
    {
        var behind = _world.Session.World.Chunks.Where(c => c.MutationLevel < Log.Level).ToList();
        foreach (var chunk in behind) CatchUp(chunk);
        return behind.Count;
    }

    private void CatchUp(Chunk chunk)
    {
        var steps = Log.Steps;
        var before = chunk.Capture();
        var after = MutationLog.Apply(before, chunk.Coord, steps, chunk.MutationLevel);
        chunk.MutationLevel = steps.Count;
        if (ReferenceEquals(before, after)) return;
        var session = _world.Session;
        var batch = session.World.BeginEdit();
        var origin = ChunkMath.ChunkOrigin(chunk.Coord);
        for (int i = 0; i < ChunkMath.Volume; i++)
        {
            ushort to = after.GetBlock(i);
            if (to == before.GetBlock(i)) continue;
            int lx = i & (ChunkMath.Size - 1), lz = (i >> ChunkMath.Shift) & (ChunkMath.Size - 1), ly = i >> (ChunkMath.Shift * 2);
            batch.SetBlock(origin.X + lx, origin.Y + ly, origin.Z + lz, to, before.Shapes?[i] ?? 0);
        }
        _world.Streamer.MarkDirty(session.Commit(batch), urgent: false);
    }
}
