using Strata.Core.Content.Defs;
using Strata.Core.Gameplay.Entities;

namespace Strata.Core.Presentation;

/// <summary>
/// What a boss bar shows: the leading boss's title and head icon (its enemy), and the group's life. <see cref="Group"/>
/// tells groups apart from frame to frame: the defeat flag they fight under, or the leading body itself.
/// </summary>
public readonly record struct BossBarState(string Title, EnemyDef Lead, long Life, long Max, object? Group = null)
{
    /// <summary>The head icon's id (<see cref="Content.AssetPaths.BossIcon"/> of the lead).</summary>
    public string IconId => Lead.Id;

    /// <summary>Life left as a fraction of the group's maximum (0–1).</summary>
    public float Fraction => Max > 0 ? Math.Clamp((float)Life / Max, 0f, 1f) : 0f;
}

/// <summary>
/// The boss bars as in Terraria: one bar per boss group alive at once, up to <see cref="MaxBars"/> stacked (the mechanical
/// bosses together, two event minibosses), each for the strongest living boss of its group (by maximum life, a worm
/// counted whole) and shared by everything fighting under its defeat flag. A pair (the Twins) adds up both; a worm cut
/// in pieces counts its arrival life once; a boss's parts count only when flagged <see cref="BossPart.InBossBar"/>
/// (Golem's head, Moon Lord's head and hands — never Skeletron's hands), and a destroyed part drops out of the sum. Minor
/// bosses (Wyverns, Devourers, the Celestial Pillars) get no bar. Allocation-free: the HUD runs it every frame.
/// </summary>
public static class BossBarModel
{
    /// <summary>Bars shown at once at most.</summary>
    public const int MaxBars = 3;

    /// <summary>The bar of the largest boss group alive (the bottom one of the stack), or null when no boss is up.</summary>
    public static BossBarState? Compute(IReadOnlyList<Npc> npcs)
    {
        BossBarState? best = null;
        for (int i = 0; i < npcs.Count; i++)
        {
            var n = npcs[i];
            if (!n.Alive || !n.IsBoss || !LeadsGroup(npcs, i)) continue;
            var bar = Bar(npcs, n);
            if (best == null || bar.Max > best.Value.Max) best = bar;
        }
        return best;
    }

    /// <summary>
    /// Fills <paramref name="into"/> (cleared first) with one bar per boss group alive, the largest group first, at most
    /// <paramref name="max"/>; returns how many.
    /// </summary>
    public static int Compute(IReadOnlyList<Npc> npcs, List<BossBarState> into, int max = MaxBars)
    {
        into.Clear();
        for (int i = 0; i < npcs.Count; i++)
        {
            var n = npcs[i];
            if (!n.Alive || !n.IsBoss || !LeadsGroup(npcs, i)) continue;
            var bar = Bar(npcs, n);
            int at = into.Count;
            while (at > 0 && into[at - 1].Max < bar.Max) at--; // larger groups first; ties keep their arrival order
            if (at >= max) continue;
            into.Insert(at, bar);
            if (into.Count > max) into.RemoveAt(into.Count - 1);
        }
        return into.Count;
    }

    /// <summary>
    /// <paramref name="n"/> outranks <paramref name="other"/> as a group's lead: a body outranks a part; among bodies (or
    /// parts) the larger leads, and on a tie the earlier in the list (<paramref name="nFirst"/>).
    /// </summary>
    private static bool Outranks(Npc n, Npc other, bool nFirst)
    {
        if ((n.Part == null) != (other.Part == null)) return n.Part == null;
        int a = Whole(n), b = Whole(other);
        return a > b || (a == b && nFirst);
    }

    /// <summary>The boss at <paramref name="index"/> leads its group: no other living boss of the group outranks it.</summary>
    private static bool LeadsGroup(IReadOnlyList<Npc> npcs, int index)
    {
        var n = npcs[index];
        for (int j = 0; j < npcs.Count; j++)
        {
            var m = npcs[j];
            if (j == index || !m.Alive || !m.IsBoss || !SameGroup(n, m)) continue;
            if (Outranks(m, n, j < index)) return false;
        }
        return true;
    }

    /// <summary>Two bosses share a bar: the same defeat flag, or (unflagged) the same body.</summary>
    private static bool SameGroup(Npc a, Npc b)
    {
        string? fa = a.Def.Boss!.Flag, fb = b.Def.Boss!.Flag;
        return fa != null || fb != null ? fa == fb : Root(a) == Root(b);
    }

    /// <summary>A part's body (while it has one), else the boss itself.</summary>
    private static Npc Root(Npc n) => n.Part != null && n.Leader != null ? n.Leader : n;

    /// <summary>The bar of <paramref name="lead"/>'s group: its title and icon, and the life of everything under it.</summary>
    private static BossBarState Bar(IReadOnlyList<Npc> npcs, Npc lead)
    {
        string? flag = lead.Def.Boss!.Flag;
        long life = 0, max = 0;
        for (int i = 0; i < npcs.Count; i++)
        {
            var n = npcs[i];
            if (!n.Alive || !InGroup(n, lead, flag)) continue;
            life += n.TotalLife;
            if (n.WholeMaxLife <= 0 || !CountedBefore(npcs, i, n.Def, lead, flag)) max += Whole(n);
        }
        return new BossBarState(lead.Def.Boss.Title, lead.Def, life, Math.Max(max, life), (object?)flag ?? Root(lead));
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
