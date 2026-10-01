namespace Strata.Core.Commands;

/// <summary>
/// Boss probes: <c>boss.&lt;enemyId&gt;.&lt;field&gt;</c> reads the first living creature of that id (0 when none
/// lives). Fields: life, max, defense, stage, phase, enraged, frenzied, illusions, parts (living fixed parts), segments.
/// </summary>
[ProbeSource]
internal static class BossProbes
{
    public static void Register(CommandContext ctx)
    {
        var w = ctx.World;
        ctx.AddPrefixProbe("boss.", spec =>
        {
            int dot = spec.LastIndexOf('.');
            if (dot <= 0) return null;
            string id = spec[..dot], field = spec[(dot + 1)..];
            if (!w.Session.Content.Enemies.Contains(id)) return null;
            var npc = w.Npcs.All.FirstOrDefault(n => n.Alive && n.Def.Id == id);
            if (npc == null) return field is "life" or "max" or "defense" or "stage" or "phase" or "enraged" or "frenzied" or "illusions" or "parts" or "segments" ? 0 : null;
            return field switch
            {
                "life" => npc.Life,
                "max" => npc.MaxLife,
                "defense" => npc.Defense,
                "stage" => npc.Stage,
                "phase" => npc.Phase,
                "enraged" => npc.Enraged ? 1 : 0,
                "frenzied" => npc.Frenzied ? 1 : 0,
                "illusions" => npc.Illusions,
                "parts" => npc.Followers.Count(f => f.Alive && f.IsPart),
                "segments" => npc.Segments.Count,
                _ => null,
            };
        });
    }
}
