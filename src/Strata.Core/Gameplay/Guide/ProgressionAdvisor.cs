using Strata.Core.Content.Defs;
using Strata.Core.Session;

namespace Strata.Core.Gameplay.Guide;

/// <summary>
/// The Guide's progression advice (data/guide/steps.json): which steps a player has done, which are open (every
/// <c>after</c> step done and every <c>showWhen</c> trigger met) and the next ones by order. Conditions are achievement
/// triggers, evaluated against the player's journal and the world. Feeds the Guide's Help and the book's checklist.
/// </summary>
public sealed class ProgressionAdvisor
{
    /// <summary>The Guide's line when every step is done.</summary>
    public const string AllDone = "You've done everything I can think of. Go show the world who is boss!";

    private readonly GameWorld _world;
    private readonly List<Compiled> _steps = new();
    private readonly Dictionary<string, Compiled> _byId = new(StringComparer.Ordinal);

    public ProgressionAdvisor(GameWorld world)
    {
        _world = world;
        foreach (var def in world.Session.Content.GuideSteps.All.OrderBy(s => s.Order).ThenBy(s => s.Id, StringComparer.Ordinal))
        {
            try
            {
                var step = new Compiled(def, def.DoneWhen.Select(GuideTrigger.Compile).ToArray(), def.ShowWhen.Select(GuideTrigger.Compile).ToArray());
                _steps.Add(step);
                _byId[def.Id] = step;
            }
            catch (Exception e) when (e is System.Text.Json.JsonException or InvalidOperationException)
            {
                Diagnostics.LogExtensions.Warn(world.Session.Log, "guide", $"guide step {def.Id}: {e.Message}"); // reported by the validator
            }
        }
    }

    /// <summary>A step's state for one player.</summary>
    public readonly record struct StepState(GuideStepDef Step, bool Done, bool Available);

    /// <summary>Every step in order, with whether it is done and whether it is open.</summary>
    public List<StepState> Steps(PlayerEntity player)
    {
        var done = new Dictionary<string, bool>(StringComparer.Ordinal);
        var list = new List<StepState>(_steps.Count);
        foreach (var s in _steps) list.Add(new StepState(s.Def, IsDone(s, player, done), IsAvailable(s, player, done)));
        return list;
    }

    /// <summary>The first <paramref name="count"/> open steps not yet done, by order.</summary>
    public List<GuideStepDef> Next(PlayerEntity player, int count = 1)
    {
        var result = new List<GuideStepDef>(count);
        foreach (var s in Steps(player))
        {
            if (result.Count >= count) break;
            if (s.Available && !s.Done) result.Add(s.Step);
        }
        return result;
    }

    /// <summary>The Guide's Help line: the next step's text.</summary>
    public string Advice(PlayerEntity player) => Next(player) is [var step, ..] ? step.Text : AllDone;

    public bool IsDone(string stepId, PlayerEntity player) =>
        _byId.TryGetValue(stepId, out var s) && IsDone(s, player, new Dictionary<string, bool>(StringComparer.Ordinal));

    private bool IsDone(Compiled s, PlayerEntity player, Dictionary<string, bool> memo)
    {
        if (memo.TryGetValue(s.Def.Id, out bool known)) return known;
        bool done = true;
        foreach (var t in s.DoneWhen) done &= t.Met(_world, player);
        return memo[s.Def.Id] = done;
    }

    private bool IsAvailable(Compiled s, PlayerEntity player, Dictionary<string, bool> memo)
    {
        foreach (var after in s.Def.After)
            if (_byId.TryGetValue(after, out var prior) && !IsDone(prior, player, memo)) return false;
        foreach (var t in s.ShowWhen)
            if (!t.Met(_world, player)) return false;
        return true;
    }

    private sealed record Compiled(GuideStepDef Def, GuideTrigger[] DoneWhen, GuideTrigger[] ShowWhen);
}
