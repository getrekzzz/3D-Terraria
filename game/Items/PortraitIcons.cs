using System.Collections.Generic;
using Godot;
using Strata.Core.Content;
using Strata.Core.Content.Defs;

namespace Strata.Game.Items;

/// <summary>
/// Town NPC head portraits and bestiary thumbnails (tools/blender/build_all.py --portraits) and boss-bar head icons
/// (--boss-icons), loaded on demand like <see cref="ItemIcons"/>. <see cref="Preload"/> requests a list in the
/// background and <see cref="Pump"/> collects finished loads within a per-frame budget, so opening a book page or the
/// housing menu does not hitch. A missing file returns null (callers fall back to text).
/// </summary>
public static class PortraitIcons
{
    private static readonly Dictionary<string, Texture2D?> Cache = new();
    private static readonly List<string> Pending = new();

    public static Texture2D? TownNpc(TownNpcDef npc) => Get(AssetPaths.TownNpcHead(npc));

    public static Texture2D? Creature(EnemyDef enemy) => Get(AssetPaths.CreatureIcon(enemy));

    public static Texture2D? Boss(EnemyDef boss) => Get(AssetPaths.BossIcon(boss));

    /// <summary>A portrait by project path; blocks only when it was neither cached nor preloaded.</summary>
    public static Texture2D? Get(string projectPath)
    {
        if (Cache.TryGetValue(projectPath, out var tex)) return tex;
        var path = "res://" + projectPath;
        if (Pending.Remove(projectPath) && ResourceLoader.LoadThreadedGetStatus(path) != ResourceLoader.ThreadLoadStatus.InvalidResource)
            tex = ResourceLoader.LoadThreadedGet(path) as Texture2D;
        else
            tex = ResourceLoader.Exists(path) ? GD.Load<Texture2D>(path) : null;
        Cache[projectPath] = tex;
        return tex;
    }

    /// <summary>Starts background loads for portraits not yet cached (e.g. every resident when the housing menu opens).</summary>
    public static void Preload(IEnumerable<string> projectPaths)
    {
        foreach (var p in projectPaths)
        {
            if (Cache.ContainsKey(p) || Pending.Contains(p)) continue;
            var path = "res://" + p;
            if (!ResourceLoader.Exists(path)) { Cache[p] = null; continue; }
            if (ResourceLoader.LoadThreadedRequest(path) == Error.Ok) Pending.Add(p);
        }
    }

    /// <summary>Moves up to <paramref name="budget"/> finished background loads into the cache; call once per frame.</summary>
    public static void Pump(int budget = 4)
    {
        for (int i = Pending.Count - 1; i >= 0 && budget > 0; i--)
        {
            var p = Pending[i];
            var status = ResourceLoader.LoadThreadedGetStatus("res://" + p);
            if (status == ResourceLoader.ThreadLoadStatus.InProgress) continue;
            Pending.RemoveAt(i);
            Cache[p] = status == ResourceLoader.ThreadLoadStatus.Loaded ? ResourceLoader.LoadThreadedGet("res://" + p) as Texture2D : null;
            budget--;
        }
    }
}
