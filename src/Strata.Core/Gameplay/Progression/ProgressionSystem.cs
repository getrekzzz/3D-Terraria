using Strata.Core.Content.Defs;
using Strata.Core.Session;

namespace Strata.Core.Gameplay.Progression;

/// <summary>
/// World progression flags: the single place flags are raised (boss defeats, event completions, commands). Raising a
/// flag also raises every <see cref="ProgressionFlagDef.AutoSet"/> flag whose requirements are now all met (the
/// warden-of-the-underworld's defeat turns the world to hardmode), and announces flags that carry a message.
/// </summary>
public sealed class ProgressionSystem
{
    private readonly GameWorld _world;

    public ProgressionSystem(GameWorld world) => _world = world;

    /// <summary>A flag was raised for the first time (announcements, world mutations).</summary>
    public event Action<ProgressionFlagDef>? Raised;

    public bool IsSet(string id) => _world.Session.Flags.Contains(id);

    /// <summary>Raises a flag; returns false when it was already set. Unknown ids are rejected by content validation.</summary>
    public bool Set(string id)
    {
        if (!_world.Session.Flags.Add(id)) return false;
        var content = _world.Session.Content;
        if (content.Flags.TryGet(id, out var def)) Raised?.Invoke(def);
        foreach (var next in content.Flags.All)
            if (next.AutoSet && !IsSet(next.Id) && next.Requires.All(IsSet)) Set(next.Id);
        return true;
    }

    /// <summary>Clears a flag (commands and tests); nothing that already happened is undone.</summary>
    public bool Clear(string id) => _world.Session.Flags.Remove(id);
}
