using System.Numerics;
using Strata.Core.Content.Defs;
using Strata.Core.Gameplay.Items;
using Strata.Core.Physics;
using Strata.Core.Session;
using Strata.Core.World;

namespace Strata.Core.Gameplay.Progression;

/// <summary>
/// Bosses called by burning an item rather than using it: a dropped item with an <c>incinerationSummon</c> behaviour
/// destroyed by a liquid (lava) in the right world layer sacrifices its resident, if any, and brings the boss.
/// </summary>
public sealed class IncinerationSummons
{
    private readonly GameWorld _world;

    public IncinerationSummons(GameWorld world)
    {
        _world = world;
        world.LiquidEffects.ItemDestroyed += (item, _) => Burned(item);
    }

    /// <summary>The boss rose from the flames (item, boss).</summary>
    public event Action<ItemDef, Entities.Npc>? Summoned;

    /// <summary>Handles an item consumed by a liquid; returns the boss if one was summoned.</summary>
    public Entities.Npc? Burned(DroppedItem item)
    {
        var def = item.Stack.Item;
        if (def == null || _world.Uses[def].Incineration is not { } p) return null;
        var session = _world.Session;
        var cell = VoxelMotion.CellOf(item.Position);
        var layer = Enum.Parse<WorldLayer>(p.Layer, ignoreCase: true);
        if (session.Generator.Plan.Bands.LayerAt(cell.Y) != layer) return null;
        if (!session.Content.Enemies.TryGet(p.Enemy, out var boss) || _world.Npcs.All.Any(n => n.Def == boss && n.Alive)) return null;
        if (p.KillsResident != null)
        {
            if (_world.Town.Find(p.KillsResident) is not { Alive: true } victim) return null;
            _world.Town.Slay(victim);
        }
        var npc = _world.Npcs.Spawn(boss, item.Position + new Vector3(0, 2f, 0));
        if (npc != null) Summoned?.Invoke(def, npc);
        return npc;
    }
}
