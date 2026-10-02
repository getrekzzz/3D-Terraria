using Strata.Core.Common;
using Strata.Core.Content.Defs;
using Strata.Core.Voxels.TileObjects;

namespace Strata.Core.Commands;

/// <summary><c>sign</c>: reads or writes the nearest sign (X-03); probe <c>signs.text</c> (its text length).</summary>
public static class SignCommands
{
    private const int Reach = 12;

    public static void Register(CommandRegistry r)
    {
        r.Register(new CommandDef
        {
            Name = "sign", Usage = "sign [text… | clear]",
            Help = "Shows what the nearest sign (within 12 cells) says, or writes it ('|' starts a new line; 'clear' wipes it).",
            Run = (ctx, a) =>
            {
                if (Nearest(ctx) is not { } sign) return CommandResult.Fail("No sign nearby.");
                if (a.Count == 0) return CommandResult.Success(sign.SignText is { } t ? $"\"{t}\"" : "The sign is blank.");
                string text = a.Rest(0);
                ctx.World.EditSign(sign, text == "clear" ? null : text.Replace('|', '\n'));
                return CommandResult.Success(sign.SignText is { } now ? $"The sign now says \"{now}\"." : "The sign is blank.");
            },
        });
    }

    internal static TileObjectInstance? Nearest(CommandContext ctx)
    {
        var c = ctx.Player.FeetCell;
        var box = new Aabb3i(c - new Int3(Reach, Reach, Reach), c + new Int3(Reach, Reach, Reach));
        TileObjectInstance? best = null;
        long bestD = long.MaxValue;
        foreach (var o in ctx.World.Session.World.TileObjects.InBox(box))
        {
            if (o.Def.Interaction != TileInteraction.Read) continue;
            var d = o.Anchor - c;
            long dist = (long)d.X * d.X + (long)d.Y * d.Y + (long)d.Z * d.Z;
            if (dist < bestD) (best, bestD) = (o, dist);
        }
        return best;
    }
}

[ProbeSource]
internal static class SignProbes
{
    public static void Register(CommandContext ctx) =>
        ctx.AddProbe("signs.text", () => SignCommands.Nearest(ctx)?.SignText?.Length ?? 0);
}
