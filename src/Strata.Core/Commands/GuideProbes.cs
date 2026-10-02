using Strata.Core.Gameplay.Guide;

namespace Strata.Core.Commands;

/// <summary>
/// Journal probes for the local player: bestiary.percent (0–100), bestiary.&lt;enemy&gt; (kills counted for the entry),
/// bestiary.&lt;enemy&gt;.unlock (0 none … 3 full), journal.&lt;obtained|crafted|seen|caught|met&gt;.&lt;id&gt; (1/0) and
/// journal.counter.&lt;key&gt;.
/// </summary>
[ProbeSource]
internal static class GuideProbes
{
    public static void Register(CommandContext ctx)
    {
        var w = ctx.World;
        ctx.AddProbe("bestiary.percent", () => w.LocalPlayer?.State.Journal.BestiaryPercent ?? 0);
        ctx.AddPrefixProbe("bestiary.", spec =>
        {
            if (w.LocalPlayer?.State.Journal is not { } j) return null;
            var parts = spec.Split('.');
            var b = w.Journal.Bestiary;
            if (!b.Contains(parts[0])) return null;
            if (parts.Length == 1) return b.Kills(parts[0], j);
            if (parts is [_, "unlock"]) return (int)b.Unlock(b.Entries.First(e => e.Id == parts[0]), j);
            return null;
        });
        // achievement.<id>: 1 once the local player has unlocked it.
        ctx.AddPrefixProbe("achievement.", id =>
            w.Session.Content.Achievements.Contains(id) ? (w.LocalPlayer?.State.Journal.Achieved(id) == true ? 1 : 0) : null);
        ctx.AddProbe("achievements", () => w.LocalPlayer?.State.Journal.Achievements.Count ?? 0);
        // guide.next.<step>: 1 while that step is the Guide's advice; guide.done.<step>: 1 once done.
        ctx.AddPrefixProbe("guide.", spec =>
        {
            if (w.LocalPlayer is not { } p) return null;
            if (spec.StartsWith("next.", StringComparison.Ordinal)) return w.Advisor.Next(p) is [var s, ..] && s.Id == spec[5..] ? 1 : 0;
            if (spec.StartsWith("done.", StringComparison.Ordinal)) return w.Advisor.IsDone(spec[5..], p) ? 1 : 0;
            return null;
        });
        ctx.AddPrefixProbe("journal.", spec =>
        {
            if (w.LocalPlayer?.State.Journal is not { } j) return null;
            int dot = spec.IndexOf('.');
            if (dot < 0) return null;
            string id = spec[(dot + 1)..];
            return spec[..dot] switch
            {
                "obtained" => j.HasObtained(id) ? 1 : 0,
                "crafted" => j.HasCrafted(id) ? 1 : 0,
                "seen" => j.Seen.Contains(id) ? 1 : 0,
                "caught" => j.Caught.Contains(id) ? 1 : 0,
                "met" => j.HasMet(id) ? 1 : 0,
                "kills" => j.Kills(id),
                "counter" => j.Counter(id),
                _ => null,
            };
        });
    }
}
