namespace Strata.Core.Commands;

/// <summary>
/// Hint probes: <c>hints.shown</c> (hints shown to the player), <c>hints.enabled</c>, <c>hint.&lt;id&gt;</c> (1 once shown)
/// and <c>hint.&lt;id&gt;.due</c> (1 while its conditions hold).
/// </summary>
[ProbeSource]
internal static class HintProbes
{
    public static void Register(CommandContext ctx)
    {
        var w = ctx.World;
        ctx.AddProbe("hints.shown", () => ctx.Player.State.Journal.HintsShown.Count);
        ctx.AddProbe("hints.enabled", () => w.Hints.Enabled ? 1 : 0);
        ctx.AddPrefixProbe("hint.", spec =>
        {
            var parts = spec.Split('.');
            if (!w.Session.Content.Hints.TryGet(parts[0], out var def)) return null;
            if (parts.Length == 1) return ctx.Player.State.Journal.HintsShown.Contains(def.Id) ? 1 : 0;
            if (parts is [_, "due"]) return w.Hints.IsDue(ctx.Player, def) ? 1 : 0;
            return null;
        });
    }
}
