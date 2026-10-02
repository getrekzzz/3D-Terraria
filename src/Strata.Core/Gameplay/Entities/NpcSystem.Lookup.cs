namespace Strata.Core.Gameplay.Entities;

public sealed partial class NpcSystem
{
    /// <summary>The live creature with this id (a damage event's <see cref="DamageEvent.NpcId"/>), or null.</summary>
    public Npc? Find(long id)
    {
        if (id == 0) return null;
        foreach (var n in _npcs)
            if (n.Id == id) return n;
        return null;
    }
}
