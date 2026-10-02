using System.Collections.Generic;
using Strata.Game.Rendering;

namespace Strata.Game.Entities;

/// <summary>
/// Pool pre-warming: hidden visuals of creatures likely to appear soon (see <see cref="AssetPreloader"/>) are
/// instantiated ahead of time, one per frame, so meeting them takes a pooled visual instead of a fresh instance.
/// </summary>
public sealed partial class NpcRenderer
{
    private readonly Dictionary<string, int> _made = new();
    private readonly Queue<string> _prewarm = new();
    private readonly HashSet<string> _prewarmQueued = new();

    /// <summary>Visuals instantiated ahead of need (probe <c>npcs.prewarmed</c>).</summary>
    public int PrewarmedCount { get; private set; }

    /// <summary>Asks for at least <paramref name="count"/> visuals of a model to exist (pooled when unused).</summary>
    public void Prewarm(string model, int count)
    {
        _made.TryGetValue(model, out int made);
        for (int i = made; i < count; i++)
            if (_prewarmQueued.Add(model + "#" + i)) _prewarm.Enqueue(model);
    }

    private void CountMade(string model) => _made[model] = _made.TryGetValue(model, out int n) ? n + 1 : 1;

    /// <summary>Instantiates at most one queued visual (models still loading go to the back of the queue).</summary>
    private void PrewarmStep()
    {
        for (int tries = _prewarm.Count; tries > 0; tries--)
        {
            var model = _prewarm.Dequeue();
            var path = $"assets/models/creatures/{model}.glb";
            using var profile = Strata.Core.Diagnostics.Metrics.Frame.Time("gd.npcs.prewarm");
            var visual = CreatureVisual.Create(path, _props);
            if (visual == null)
            {
                if (AssetCache.IsPending("res://" + path) || HumanAnimations.SetPath(path) is { } set && AssetCache.IsPending(set))
                    _prewarm.Enqueue(model);
                continue;
            }
            CountMade(model);
            PrewarmedCount++;
            AddChild(visual);
            Release(visual, model);
            return;
        }
    }
}
