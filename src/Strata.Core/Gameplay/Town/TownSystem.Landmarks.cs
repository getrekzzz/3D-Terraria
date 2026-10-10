using Strata.Core.Common;
using Strata.Core.Conditions;
using Strata.Core.Content.Defs;
using Strata.Core.Voxels;

namespace Strata.Core.Gameplay.Town;

/// <summary>
/// Residents who live at a planned landmark rather than in a house (the keeper at the crypt gate), and residents who
/// leave for good once their <see cref="TownNpcDef.LeavesWhen"/> conditions hold.
/// </summary>
public sealed partial class TownSystem
{
    private const int LandmarkInterval = 60;

    private readonly Dictionary<string, ICondition> _leaves = new();

    /// <summary>A resident left town for good (HUD announcement).</summary>
    public event Action<TownNpc>? Departed;

    /// <summary>The planned cell of a landmark resident's place (null for housed residents or a world without it).</summary>
    public Int3? LandmarkOf(TownNpcDef def) =>
        def.Landmark != null && _world.Session.Generator.Plan.Landmarks.TryGetValue(def.Landmark, out var cell) ? cell : null;

    private Int3? LandmarkNode(TownNpcDef def) =>
        LandmarkOf(def) is { } cell ? _world.Navigator.NearestStandable(cell, Agent, 8) : null;

    private bool HasLeft(TownNpcDef def, ConditionContext ctx) => def.LeavesWhen.Length > 0 && _leaves[def.Id].Evaluate(ctx);

    /// <summary>Sends away residents whose leave conditions hold and places landmark residents once their spot is loaded.</summary>
    public void UpdateLandmarks()
    {
        var player = _world.LocalPlayer;
        if (player == null) return;
        var ctx = TownContext(player);
        for (int i = _residents.Count - 1; i >= 0; i--)
        {
            var r = _residents[i];
            if (!HasLeft(r.Def, ctx)) continue;
            _residents.RemoveAt(i);
            Removed?.Invoke(r);
            Departed?.Invoke(r);
        }
        foreach (var def in _world.Session.Content.TownNpcs.All)
        {
            if (LandmarkOf(def) is not { } spot || Find(def.Id) != null || HasLeft(def, ctx)) continue;
            if (World.GetBlock(spot) == VoxelWorld.Unknown) continue; // wait until the place is streamed in
            if (LandmarkNode(def) is { } node) Create(def, node);
        }
    }
}
