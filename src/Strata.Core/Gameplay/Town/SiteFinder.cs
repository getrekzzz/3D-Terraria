using System.Numerics;
using Strata.Core.Common;
using Strata.Core.Conditions;
using Strata.Core.Determinism;
using Strata.Core.Gameplay.Navigation;
using Strata.Core.Session;
using Strata.Core.Voxels;
using Strata.Core.WorldGen.Features;

namespace Strata.Core.Gameplay.Town;

/// <summary>
/// Finds a place for something to appear unseen (bound NPCs, later visitors): a standable node in a loaded chunk where
/// the conditions hold and the player cannot see it. Rare zones (spider nests, the Dungeon) are sampled inside the
/// planned features carrying that zone; anything else in a shell around the player. At most
/// <see cref="MaxCandidates"/> cells are tried per call.
/// </summary>
public sealed class SiteFinder
{
    public const int MaxCandidates = 8;

    /// <summary>Vertical spread of the shell around the player (m).</summary>
    public const float VerticalSpread = 20f;

    private readonly GameWorld _world;
    private readonly List<WorldFeature> _features = new();

    public SiteFinder(GameWorld world) => _world = world;

    /// <param name="zones">Feature zones to sample inside (empty = the shell around the player).</param>
    public Int3? TryFind(ICondition where, IReadOnlyList<string> zones, float minDistance, float maxDistance, PlayerEntity player, ref Pcg32 rng)
    {
        NearbyFeatures(zones, maxDistance * 3f, player);
        var ctx = new ConditionContext();
        for (int i = 0; i < MaxCandidates; i++)
        {
            Int3 cell;
            if (_features.Count > 0)
            {
                var b = _features[rng.Range(0, _features.Count - 1)].Bounds;
                cell = new Int3(rng.Range(b.Min.X, b.Max.X - 1), rng.Range(b.Min.Y, b.Max.Y - 1), rng.Range(b.Min.Z, b.Max.Z - 1));
            }
            else
            {
                float angle = rng.Range(0f, MathF.Tau), dist = rng.Range(minDistance, maxDistance), dy = rng.Range(-VerticalSpread, VerticalSpread);
                var at = player.Position + new Vector3(MathF.Cos(angle) * dist, dy, MathF.Sin(angle) * dist);
                cell = new Int3((int)MathF.Floor(at.X / Units.CellSize), (int)MathF.Floor(at.Y / Units.CellSize), (int)MathF.Floor(at.Z / Units.CellSize));
            }
            if (Accept(cell, where, ctx, player) is { } node) return node;
        }
        return null;
    }

    /// <summary>A standable, unseen node near a cell where the conditions hold (null otherwise).</summary>
    public Int3? Accept(Int3 cell, ICondition where, ConditionContext ctx, PlayerEntity player, int radius = 6)
    {
        var world = _world.Session.World;
        if (!world.InBounds(cell.X, cell.Y, cell.Z) || world.GetBlock(cell) == VoxelWorld.Unknown) return null;
        if (!where.Evaluate(_world.Session.ContextAt(cell, ctx))) return null;
        if (_world.Navigator.NearestStandable(cell, NavAgent.Humanoid, radius) is not { } node) return null;
        var center = TownSystem.NodeFeet(node) + new Vector3(0, TownNpc.HalfExtents.Y, 0);
        return _world.Npcs.InSight(player.Eye, center) ? null : node;
    }

    private void NearbyFeatures(IReadOnlyList<string> zones, float reach, PlayerEntity player)
    {
        _features.Clear();
        if (zones.Count == 0) return;
        foreach (var f in _world.Session.Generator.Plan.Features.All)
        {
            if (f.Zone == null || !Contains(zones, f.Zone)) continue;
            var b = f.Bounds;
            float cx = (b.Min.X + b.Max.X) * 0.5f * Units.CellSize - player.Position.X, cz = (b.Min.Z + b.Max.Z) * 0.5f * Units.CellSize - player.Position.Z;
            if (cx * cx + cz * cz <= reach * reach) _features.Add(f);
        }
    }

    private static bool Contains(IReadOnlyList<string> list, string id)
    {
        for (int i = 0; i < list.Count; i++)
            if (list[i] == id) return true;
        return false;
    }
}
