using System.Text.Json;
using Strata.Core.Common;
using Strata.Core.Conditions;
using Strata.Core.Content;
using Strata.Core.Content.Defs;
using Strata.Core.Session;
using Strata.Core.Voxels;

namespace Strata.Core.Gameplay.Town;

/// <summary>
/// Bound NPCs (Terraria's rescues): while an NPC's rescue flag is unset and its requirements hold, it appears bound
/// at an unseen site (<see cref="SiteFinder"/>) or its planned landmark; talking to it frees it, after which it is a
/// homeless resident whom the next move-in check houses.
/// </summary>
public sealed partial class TownSystem
{
    public const int RescueInterval = 120;

    private readonly Dictionary<string, (ICondition Requires, ICondition Where, string[] Zones)> _rescues = new();
    private SiteFinder? _sites;

    /// <summary>A bound NPC was freed (announcement, effects).</summary>
    public event Action<TownNpc, PlayerEntity>? Rescued;

    private SiteFinder Sites => _sites ??= new SiteFinder(_world);

    private void CompileRescue(TownNpcDef def)
    {
        if (def.Rescue is not { } r) return;
        _rescues[def.Id] = (ConditionCompiler.CompileAll(r.Requires), ConditionCompiler.CompileAll(r.Where), ZonesOf(r.Where));
    }

    /// <summary>Biome ids named by the where-conditions' "biome any" lists (sampled inside plan features carrying them).</summary>
    private static string[] ZonesOf(TypedSpec[] where)
    {
        var zones = new List<string>();
        foreach (var s in where)
            if (s.Type == "biome" && s.Raw.TryGetProperty("any", out var any) && any.ValueKind == JsonValueKind.Array)
                foreach (var z in any.EnumerateArray())
                    if (z.GetString() is { } id) zones.Add(id);
        return zones.ToArray();
    }

    /// <summary>Places bound NPCs whose time has come (every <see cref="RescueInterval"/> ticks).</summary>
    public void UpdateRescues()
    {
        var player = _world.LocalPlayer;
        if (player == null || _rescues.Count == 0) return;
        ConditionContext? ctx = null;
        foreach (var def in _world.Session.Content.TownNpcs.All)
        {
            if (def.Rescue is not { } info || !_rescues.TryGetValue(def.Id, out var c)) continue;
            if (_world.Session.Flags.Contains(TownRescue.FlagFor(def.Id)) || Find(def.Id) != null) continue;
            ctx ??= TownContext(player);
            if (!c.Requires.Evaluate(ctx)) continue;
            var node = info.Landmark != null ? LandmarkSite(info.Landmark, c.Where, player) : Sites.TryFind(c.Where, c.Zones, info.MinDistance, info.MaxDistance, player, ref _rng);
            if (node is { } n) Bind(def, n);
        }
    }

    private Int3? LandmarkSite(string landmark, ICondition where, PlayerEntity player)
    {
        if (!_world.Session.Generator.Plan.Landmarks.TryGetValue(landmark, out var cell) || World.GetBlock(cell) == VoxelWorld.Unknown) return null;
        // A planned site (the Dungeon's Mechanic room) is reserved for them: they wait there even in plain view.
        return Sites.Accept(cell + Int3.Up, where, new ConditionContext(), player, radius: 8, unseen: false);
    }

    /// <summary>Places an NPC bound at a node (commands and tests skip the requirements).</summary>
    public TownNpc Bind(TownNpcDef def, Int3 node)
    {
        var npc = Find(def.Id) ?? Create(def, node, bound: true);
        npc.Bound = true;
        npc.Posture = TownNpc.BoundPosture(def.Rescue?.Pose);
        return npc;
    }

    /// <summary>Frees a bound NPC: sets their rescue flag; a required item (Purification Powder) is used up.</summary>
    public bool Rescue(TownNpc npc, PlayerEntity player, out string message)
    {
        message = "";
        if (!npc.Bound || !npc.Alive) return false;
        var info = npc.Def.Rescue;
        if (info?.RequiresItem is { } itemId && _world.Session.Content.Items.TryGet(itemId, out var item))
        {
            var bag = player.State.Inventory.Backpack;
            if (bag.Count(item) < 1)
            {
                message = info.NeedsItemText.Replace("{item}", item.DisplayName);
                return false;
            }
            bag.Remove(d => d == item, 1);
        }
        _world.Progression.Set(TownRescue.FlagFor(npc.Def.Id));
        npc.Bound = false;
        npc.Posture = ResidentPosture.Stand;
        npc.Activity = ResidentActivity.Idle;
        npc.IdleTicks = Config.IdleTicksMin;
        message = (info?.Thanks ?? "").Replace("{player}", player.State.Name);
        Rescued?.Invoke(npc, player);
        _world.Npcs.Broadcast($"{npc.Title} has been rescued!", Messages.MessageKind.Town);
        return true;
    }
}
