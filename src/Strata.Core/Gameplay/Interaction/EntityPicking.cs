using System.Numerics;
using Strata.Core.Gameplay.Entities;
using Strata.Core.Gameplay.Town;
using Strata.Core.Session;

namespace Strata.Core.Gameplay.Interaction;

/// <summary>
/// A creature or town resident under the crosshair: exactly one of <see cref="Npc"/> and <see cref="Resident"/> is set.
/// <see cref="Distance"/> is how far along the ray (m from its origin) the ray enters the padded body.
/// </summary>
public readonly record struct EntityPick(Npc? Npc, TownNpc? Resident, float Distance);

/// <summary>
/// Which creature or town resident a ray points at (the crosshair names, talking to residents): a padded box test
/// against every live creature's body and worm segments (a segment resolves to the creature it belongs to) and every
/// live resident. Allocation-free, so it runs every physics tick: lists are walked by index and volumes come from
/// <see cref="Npc.VolumeAt"/>. The caller passes the terrain hit as <c>maxT</c>, so walls hide what is behind them.
/// </summary>
public static class EntityPicking
{
    /// <summary>How much (m) every box is grown so small and fast creatures are easy to point at.</summary>
    public const float DefaultPad = 0.1f;

    /// <summary>
    /// The nearest creature or resident the ray from <paramref name="origin"/> along <paramref name="dir"/> enters
    /// between <paramref name="minT"/> and <paramref name="maxT"/> metres (a body the <paramref name="minT"/> plane
    /// cuts counts at <paramref name="minT"/>), or null. Removed and dead bodies are skipped.
    /// </summary>
    public static EntityPick? Pick(GameWorld w, Vector3 origin, Vector3 dir, float minT, float maxT, float pad = DefaultPad) =>
        Pick(w.Npcs.All, w.Town.Residents, origin, dir, minT, maxT, pad);

    /// <inheritdoc cref="Pick(GameWorld, Vector3, Vector3, float, float, float)"/>
    public static EntityPick? Pick(IReadOnlyList<Npc> npcs, IReadOnlyList<TownNpc> residents, Vector3 origin, Vector3 dir, float minT, float maxT, float pad = DefaultPad)
    {
        if (dir.LengthSquared() < 1e-10f || maxT < minT) return null;
        dir = Vector3.Normalize(dir);
        var grow = new Vector3(pad);
        float best = maxT;
        Npc? bestNpc = null;
        TownNpc? bestResident = null;
        for (int i = 0; i < npcs.Count; i++)
        {
            var npc = npcs[i];
            if (npc.Removed || !npc.Alive) continue;
            int count = npc.VolumeCount;
            for (int v = 0; v < count; v++)
            {
                var (min, max) = npc.VolumeAt(v);
                if (!Enter(origin, dir, min - grow, max + grow, minT, best, out float t)) continue;
                best = t;
                bestNpc = npc;
                bestResident = null;
            }
        }
        for (int i = 0; i < residents.Count; i++)
        {
            var r = residents[i];
            if (!r.Alive) continue;
            var c = r.Center;
            if (!Enter(origin, dir, c - TownNpc.HalfExtents - grow, c + TownNpc.HalfExtents + grow, minT, best, out float t)) continue;
            best = t;
            bestNpc = null;
            bestResident = r;
        }
        return bestNpc != null || bestResident != null ? new EntityPick(bestNpc, bestResident, best) : null;
    }

    /// <summary>
    /// What the crosshair label shows for a pick: a resident's title ("Andrew the Guide"), a boss's title or a
    /// creature's name, with its life. Parts and followers are creatures of their own and show their own life; a worm's
    /// segments show the whole worm's.
    /// </summary>
    public static (string Name, int Life, int MaxLife) Describe(EntityPick pick)
    {
        if (pick.Resident is { } r) return (r.Title, r.Life, r.Def.Life);
        var npc = pick.Npc!;
        string name = npc.IsBoss && npc.Def.Boss!.Title != "" ? npc.Def.Boss.Title : npc.Def.DisplayName;
        return (name, npc.Life, npc.MaxLife);
    }

    /// <summary>
    /// Slab test: where (the entry t, at least <paramref name="minT"/>) the ray enters the box, if that is before
    /// <paramref name="maxT"/>. A ray parallel to a slab misses unless its origin lies within it.
    /// </summary>
    public static bool Enter(Vector3 o, Vector3 d, Vector3 min, Vector3 max, float minT, float maxT, out float t)
    {
        float near = minT, far = maxT;
        t = 0;
        if (!Slab(o.X, d.X, min.X, max.X, ref near, ref far)) return false;
        if (!Slab(o.Y, d.Y, min.Y, max.Y, ref near, ref far)) return false;
        if (!Slab(o.Z, d.Z, min.Z, max.Z, ref near, ref far)) return false;
        t = near;
        return true;
    }

    private static bool Slab(float o, float d, float min, float max, ref float near, ref float far)
    {
        if (MathF.Abs(d) < 1e-8f) return o >= min && o <= max;
        float inv = 1f / d;
        float a = (min - o) * inv, b = (max - o) * inv;
        if (a > b) (a, b) = (b, a);
        if (a > near) near = a;
        if (b < far) far = b;
        return near <= far;
    }
}
