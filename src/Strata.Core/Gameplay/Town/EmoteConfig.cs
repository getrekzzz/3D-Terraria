namespace Strata.Core.Gameplay.Town;

/// <summary>Emote bubbles and NPC voices (data/config/town.json "emotes", X-05). Ticks are 1/60 s, distances metres.</summary>
public sealed class EmoteConfig
{
    /// <summary>Every emote id; each has a bubble icon assets/icons/emotes/&lt;id&gt;.png.</summary>
    public string[] Ids { get; init; } = { "happy", "love", "angry", "sad", "sleepy", "music", "question", "alarm", "chat", "laugh", "idea" };

    /// <summary>The player's emote wheel, clockwise from the top.</summary>
    public string[] Wheel { get; init; } = { "happy", "love", "laugh", "music", "question", "sad", "angry", "sleepy" };

    /// <summary>Ticks between emote checks; each check looks at a few residents.</summary>
    public int Interval { get; init; } = 30;
    public int ResidentsPerCheck { get; init; } = 2;

    /// <summary>Chance a ready resident emotes when checked.</summary>
    public float Chance { get; init; } = 0.25f;

    /// <summary>A resident waits this long (ticks) between emotes.</summary>
    public int CooldownMin { get; init; } = 600;
    public int CooldownMax { get; init; } = 1800;

    /// <summary>How long a bubble shows (ticks).</summary>
    public int BubbleTicks { get; init; } = 150;

    /// <summary>Residents this close chat with each other and answer the player's emotes.</summary>
    public float ChatRadius { get; init; } = 4f;
    public float ReplyRadius { get; init; } = 10f;

    /// <summary>World events that frighten residents ("alarm").</summary>
    public string[] DangerEvents { get; init; } =
    {
        "blood_moon", "slime_rain", "goblin_army", "solar_eclipse", "pumpkin_moon", "frost_moon", "frost_legion",
        "pirate_invasion", "martian_madness", "old_ones_army", "lunar_events",
    };

    public string[] PartyEvents { get; init; } = { "party", "lantern_night" };
    public string[] RainEvents { get; init; } = { "rain", "thunderstorm" };

    /// <summary>Voice blips per town NPC id (sound id of tools/audio sfx_recipes_town.py and a pitch); missing = default.</summary>
    public Dictionary<string, VoiceSpec> Voices { get; init; } = new();
    public VoiceSpec DefaultVoice { get; init; } = new();

    public VoiceSpec VoiceOf(string npc) => Voices.TryGetValue(npc, out var v) ? v : DefaultVoice;
}

public sealed class VoiceSpec
{
    public string Sound { get; init; } = "voice_mid";
    public float Pitch { get; init; } = 1f;
}
