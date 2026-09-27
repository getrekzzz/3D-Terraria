using Strata.Core.Common;

namespace Strata.Core.Session;

public sealed partial class GameWorld
{
    /// <summary>Objects with an aura (campfires, heart lanterns) refresh their buff on every player within reach.</summary>
    private void ApplyAuras()
    {
        foreach (var p in Players)
        {
            if (!p.Alive) continue;
            var c = p.FeetCell;
            var box = new Aabb3i(c - new Int3(24, 24, 24), c + new Int3(24, 24, 24));
            foreach (var obj in Session.World.TileObjects.InBox(box))
            {
                if (obj.Def.Aura is not { } aura || !Session.Content.Buffs.TryGet(aura.Buff, out var buff)) continue;
                var d = obj.Anchor - c;
                if (d.X * d.X + d.Y * d.Y + d.Z * d.Z > aura.Radius * aura.Radius) continue;
                p.State.Buffs.Add(buff, aura.Ticks, p.State.Effects.Immunities);
                p.State.Recompute();
            }
        }
    }
}
