using Strata.Core.Content.Defs;
using Strata.Core.Gameplay.Entities;

namespace Strata.Core.Presentation;

/// <summary>What the boss bar shows: the leading boss's title and head icon (its enemy), and the group's life.</summary>
public readonly record struct BossBarState(string Title, EnemyDef Lead, long Life, long Max)
{
    /// <summary>The head icon's id (<see cref="Content.AssetPaths.BossIcon"/> of the lead).</summary>
    public string IconId => Lead.Id;

    /// <summary>Life left as a fraction of the group's maximum (0–1).</summary>
    public float Fraction => Max > 0 ? Math.Clamp((float)Life / Max, 0f, 1f) : 0f;
}

/// <summary>
/// The boss bar as in Terraria: one bar for the strongest living boss (by maximum life, a worm counted whole), shared by
/// everything fighting under its defeat flag. A pair (the Twins) adds up both; a worm cut in pieces counts its arrival
/// life once; a boss's parts count only when flagged <see cref="BossPart.InBossBar"/> (Golem's head, Moon Lord's head
/// and hands — never Skeletron's hands), and a destroyed part drops out of the sum. Minor bosses (Wyverns, Devourers,
/// the Celestial Pillars) get no bar. Allocation-free: the HUD runs it every frame.
/// </summary>
public static class BossBarModel
{
    public static BossBarState? Compute(IReadOnlyList<Npc> npcs)
    {
        Npc? lead = null;
        for (int i = 0; i < npcs.Count; i++)
        {
            var n = npcs[i];
            if (!n.Alive || !n.IsBoss) continue;
            // A body outranks its parts; among bodies (or, once only parts are left, among them) the larger leads.
            if (lead == null || (lead.Part != null && n.Part == null) || ((lead.Part == null) == (n.Part == null) && Whole(n) > Whole(lead)))
                lead = n;
        }
        if (lead == null) return null;
        string? flag = lead.Def.Boss!.Flag;
        long life = 0, max = 0;
        for (int i = 0; i < npcs.Count; i++)
        {
            var n = npcs[i];
            if (!n.Alive || !InGroup(n, lead, flag)) continue;
            life += n.TotalLife;
            if (n.WholeMaxLife <= 0 || !CountedBefore(npcs, i, n.Def, lead, flag)) max += Whole(n);
        }
        return new BossBarState(lead.Def.Boss.Title, lead.Def, life, Math.Max(max, life));
    }

    /// <summary>Whether <paramref name="n"/> fights under the lead's bar (shared flag, or the lead itself and its parts).</summary>
    private static bool InGroup(Npc n, Npc lead, string? flag)
    {
        if (n == lead) return true;
        if (n.Part != null && n.Leader != null && !n.InBossBar) return false; // Skeletron's hands, Golem's fists
        if (flag != null) return n.Def.Boss?.Flag == flag && !n.Def.Boss.Minor;
        return n.Leader == lead && n.InBossBar;
    }

    /// <summary>An earlier piece of the same worm already counted its whole arrival life.</summary>
    private static bool CountedBefore(IReadOnlyList<Npc> npcs, int index, EnemyDef def, Npc lead, string? flag)
    {
        for (int k = 0; k < index; k++)
        {
            var m = npcs[k];
            if (m.Alive && m.Def == def && m.WholeMaxLife > 0 && InGroup(m, lead, flag)) return true;
        }
        return false;
    }

    /// <summary>Life on arrival: the whole worm's for a segmented boss, else its maximum.</summary>
    private static int Whole(Npc n) => n.WholeMaxLife > 0 ? n.WholeMaxLife : n.MaxLife;
}
