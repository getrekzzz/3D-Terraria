using System.Numerics;
using Strata.Core.Content.Defs;
using Strata.Core.Session;

namespace Strata.Core.Gameplay.Town;

/// <summary>
/// Emote bubbles (X-05): every <see cref="EmoteConfig.Interval"/> ticks a few residents in turn may show what they feel
/// (<see cref="EmoteRules"/>: mood, loved or hated neighbours, danger, parties, rain, sleep, a chat with a neighbour,
/// who answers). The player's emotes (the wheel) are answered by residents nearby. The renderer draws the bubbles.
/// </summary>
public sealed partial class TownSystem
{
    private readonly Dictionary<long, long> _emoteReady = new();
    private int _emoteCursor;

    private EmoteConfig Emotes => Config.Emotes;

    /// <summary>A resident shows an emote bubble.</summary>
    public event Action<TownNpc, string>? Emoted;

    /// <summary>A player used the emote wheel.</summary>
    public event Action<PlayerEntity, string>? PlayerEmoted;

    /// <summary>Bubbles shown so far (probe town.emotes_shown).</summary>
    public int EmotesShown { get; private set; }

    /// <summary>Looks at the next few residents and lets a ready one emote now and then.</summary>
    public void UpdateEmotes()
    {
        int n = _residents.Count;
        if (n == 0) return;
        long tick = _world.Session.Clock.Tick;
        for (int k = 0; k < Math.Min(Emotes.ResidentsPerCheck, n); k++)
        {
            var r = _residents[_emoteCursor++ % n];
            if (!r.Alive || r.Bound || !Ready(r, tick) || _rng.NextFloat() >= Emotes.Chance) continue;
            var input = Feelings(r, out var partner);
            if (EmoteRules.Pick(input, _rng.NextFloat()) is not { } emote) continue;
            Emote(r, emote);
            if (emote == "chat" && partner != null && Ready(partner, tick))
                Emote(partner, EmoteRules.Reply(emote, _world.Commerce.Happiness(partner).Factor));
        }
    }

    /// <summary>Shows an emote over a resident (also used by commands and petting) and starts their cooldown.</summary>
    public void Emote(TownNpc r, string emote)
    {
        _emoteReady[r.Id] = _world.Session.Clock.Tick + _rng.Range(Emotes.CooldownMin, Emotes.CooldownMax);
        EmotesShown++;
        Emoted?.Invoke(r, emote);
    }

    /// <summary>The player emotes; residents within <see cref="EmoteConfig.ReplyRadius"/> who are ready answer.</summary>
    public bool PlayerEmote(PlayerEntity p, string emote)
    {
        if (Array.IndexOf(Emotes.Ids, emote) < 0) return false;
        PlayerEmoted?.Invoke(p, emote);
        long tick = _world.Session.Clock.Tick;
        foreach (var r in _residents)
            if (r.Alive && !r.Bound && Ready(r, tick) && Vector3.Distance(r.Position, p.Position) <= Emotes.ReplyRadius)
                Emote(r, EmoteRules.Reply(emote, _world.Commerce.Happiness(r).Factor));
        return true;
    }

    private bool Ready(TownNpc r, long tick) => !_emoteReady.TryGetValue(r.Id, out var at) || tick >= at;

    private EmoteInput Feelings(TownNpc r, out TownNpc? partner)
    {
        partner = null;
        var ctx = _world.Session.ContextAt(r.FeetCell);
        bool loved = false, hated = false;
        float best = Emotes.ChatRadius;
        foreach (var o in _residents)
        {
            if (o == r || !o.Alive || o.Bound) continue;
            float d = Vector3.Distance(o.Position, r.Position);
            if (d > Emotes.ReplyRadius) continue;
            foreach (var m in r.Def.Mood)
                if (m.Neighbor == o.Def.Id)
                {
                    loved |= m.Score >= 2;
                    hated |= m.Score <= -2;
                }
            if (d <= best && o.TalkingTo == null) (partner, best) = (o, d);
        }
        bool sleeping = !_world.Session.Clock.IsDay && r.Home is { } h && Vector3.Distance(r.Position, NodeFeet(h)) < 2f;
        return new EmoteInput(
            Happiness: _world.Commerce.Happiness(r).Factor,
            Sleeping: sleeping,
            Danger: Any(ctx.Events, Emotes.DangerEvents),
            Party: Any(ctx.Events, Emotes.PartyEvents),
            Raining: Any(ctx.Events, Emotes.RainEvents),
            LovedNear: loved,
            HatedNear: hated,
            NearOther: partner != null,
            TalkingToPlayer: r.TalkingTo != null,
            Pet: r.Def.Kind == TownNpcKind.Pet);
    }

    private static bool Any(IReadOnlySet<string> events, string[] ids)
    {
        if (events.Count == 0) return false;
        foreach (var id in ids)
            if (events.Contains(id)) return true;
        return false;
    }
}
