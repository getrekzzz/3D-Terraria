using Strata.Core.Common;

namespace Strata.Core.Commands;

/// <summary>
/// <c>near_object.&lt;objectId&gt;</c>: tile objects of that id overlapping the 33-cell cube round the player's feet
/// (a library's books, a strip of spikes), the object counterpart of <c>near.&lt;block&gt;</c>.
/// </summary>
[ProbeSource]
internal static class ObjectProbes
{
    public static void Register(CommandContext ctx)
    {
        var w = ctx.World;
        ctx.AddPrefixProbe("near_object.", id =>
        {
            if (!w.Session.Content.TileObjects.Contains(id)) return null;
            var f = ctx.Player.FeetCell;
            int n = 0;
            foreach (var o in w.Session.World.TileObjects.InBox(new Aabb3i(f - Int3.One * 16, f + Int3.One * 17)))
                if (o.Def.Id == id) n++;
            return n;
        });
    }
}
