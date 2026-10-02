using System.Numerics;
using Strata.Core.Common;
using Strata.Core.Content.Defs;
using Strata.Core.Session;
using Strata.Core.Voxels.TileObjects;

namespace Strata.Core.Gameplay.Town;

public enum PylonTeleport
{
    Teleported,
    NotNearPylon,
    InactiveDestination,
    SamePylon,
    NoRoom,
}

/// <summary>
/// Biome pylons (Terraria 1.4, P5-34). A resident who is content or happier and has another resident living close by
/// sells the pylon of the biome their home is in (<see cref="PylonConfig.Rules"/>). A placed pylon works while
/// <see cref="PylonConfig.MinResidents"/> residents live within <see cref="PylonConfig.ActiveRadius"/>; from near a
/// working pylon the player teleports to any other working one (the world map, or the <c>pylon</c> command).
/// </summary>
public sealed class PylonNetwork
{
    private readonly GameWorld _world;
    private readonly TownSystem _town;
    private readonly Dictionary<string, PylonRule> _rules = new();
    private readonly List<TileObjectInstance> _found = new();

    public PylonNetwork(GameWorld world, TownSystem town)
    {
        _world = world;
        _town = town;
        foreach (var r in Config.Rules) _rules[r.Object] = r;
    }

    private PylonConfig Config => _town.Config.Pylons;

    /// <summary>The player teleported between pylons (camera snap, sound).</summary>
    public event Action<PlayerEntity, TileObjectInstance>? Teleported;

    public int Teleports { get; private set; }

    public bool IsPylon(TileObjectDef def) => _rules.ContainsKey(def.Id);

    /// <summary>Every placed pylon in the loaded world, in placement order (a shared list: copy to keep).</summary>
    public IReadOnlyList<TileObjectInstance> All()
    {
        _found.Clear();
        foreach (var o in _world.Session.World.TileObjects.All)
            if (_rules.ContainsKey(o.Def.Id)) _found.Add(o);
        _found.Sort((a, b) => a.Id.CompareTo(b.Id));
        return _found;
    }

    /// <summary>Whether enough residents live around the pylon for it to work.</summary>
    public bool IsActive(TileObjectInstance pylon)
    {
        var at = Feet(pylon);
        int n = 0;
        foreach (var r in _town.Residents)
        {
            if (!r.CountsAsResident) continue;
            var p = r.Home is { } h ? TownSystem.NodeFeet(h) : r.Position;
            if (Vector3.Distance(p, at) <= Config.ActiveRadius && ++n >= Config.MinResidents) return true;
        }
        return false;
    }

    /// <summary>The working pylon the player stands near (null = none in reach).</summary>
    public TileObjectInstance? NearbyActive(PlayerEntity p)
    {
        TileObjectInstance? best = null;
        float bestD = Config.UseRadius;
        foreach (var o in All())
        {
            float d = Vector3.Distance(Feet(o), p.Position);
            if (d <= bestD && IsActive(o)) (best, bestD) = (o, d);
        }
        return best;
    }

    /// <summary>Moves the player next to <paramref name="to"/> when they stand near a working pylon and it works too.</summary>
    public PylonTeleport Teleport(PlayerEntity p, TileObjectInstance to)
    {
        if (NearbyActive(p) is not { } from) return PylonTeleport.NotNearPylon;
        if (from == to) return PylonTeleport.SamePylon;
        if (!IsActive(to)) return PylonTeleport.InactiveDestination;
        var b = to.Bounds;
        var beside = new Int3(b.Max.X, b.Min.Y, (b.Min.Z + b.Max.Z) / 2);
        if (_world.Navigator.NearestStandable(beside, Navigation.NavAgent.Humanoid, 4) is not { } node) return PylonTeleport.NoRoom;
        p.Hook.Reset();
        p.Position = TownSystem.NodeFeet(node);
        p.Velocity = Vector3.Zero;
        Teleports++;
        Teleported?.Invoke(p, to);
        return PylonTeleport.Teleported;
    }

    /// <summary>
    /// The pylon a resident sells right now (null = none): the rule matching their home's biome (or layer), when they
    /// are content or happier and another resident lives within <see cref="PylonConfig.NeighbourRadius"/>.
    /// </summary>
    public TileObjectDef? ForSale(TownNpc npc, Conditions.ConditionContext home, float priceFactor)
    {
        if (!npc.IsHoused || priceFactor > Config.MaxFactor || !HasNeighbour(npc)) return null;
        bool underground = home.Layer is World.WorldLayer.Underground or World.WorldLayer.Cavern;
        var objects = _world.Session.Content.TileObjects;
        foreach (var rule in Config.Rules)
        {
            bool match = rule.Layer == "underground" ? underground : !underground && rule.Biome != null && home.Biomes.Contains(rule.Biome);
            if (match && objects.TryGet(rule.Object, out var def)) return def;
        }
        return null;
    }

    private bool HasNeighbour(TownNpc npc)
    {
        var at = TownSystem.NodeFeet(npc.Home!.Value);
        foreach (var o in _town.Residents)
            if (o != npc && o.IsHoused && Vector3.Distance(TownSystem.NodeFeet(o.Home!.Value), at) <= Config.NeighbourRadius) return true;
        return false;
    }

    private static Vector3 Feet(TileObjectInstance o)
    {
        var b = o.Bounds;
        return new Vector3((b.Min.X + b.Max.X) * 0.5f, b.Min.Y, (b.Min.Z + b.Max.Z) * 0.5f) * Units.CellSize;
    }
}
