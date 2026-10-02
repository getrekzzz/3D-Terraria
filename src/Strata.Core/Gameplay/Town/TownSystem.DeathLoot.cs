using Strata.Core.Determinism;
using Strata.Core.Gameplay.Loot;
using Strata.Core.Physics;

namespace Strata.Core.Gameplay.Town;

public sealed partial class TownSystem
{
    private LootRoller? _deathLoot;

    /// <summary>
    /// The table a fallen resident drops from, when it exists: "&lt;resident id&gt;_death" (the Clothier's Voodoo Doll when he
    /// dies while a player wears the Red Hat). Its conditions see the spot and what the players wear ("wearing").
    /// </summary>
    public static string DeathTable(string residentId) => residentId + Content.Index.ItemSourceIndex.DeathTableSuffix;

    private void DropDeathLoot(TownNpc r)
    {
        var content = _world.Session.Content;
        string table = DeathTable(r.Def.Id);
        if (!content.LootTables.Contains(table)) return;
        var ctx = _world.Session.ContextAt(VoxelMotion.CellOf(r.Center));
        ctx.Wearing = Wears;
        _deathLoot ??= new LootRoller(content);
        var rng = new Pcg32(Hash.Combine((ulong)r.Id, _world.Session.Clock.Tick));
        foreach (var stack in _deathLoot.Roll(table, ref rng, ctx))
            _world.Drops.Spawn(stack, r.Center, new System.Numerics.Vector3(_rng.NextSigned() * 1.5f, 3f, _rng.NextSigned() * 1.5f));
    }

    private bool Wears(string item)
    {
        foreach (var p in _world.Players)
            foreach (var s in p.State.Inventory.EquippedStacks)
                if (s.Item?.Id == item) return true;
        return false;
    }
}
