namespace Strata.Core.Gameplay.Town;

/// <summary>What a resident is feeling right now, as <see cref="EmoteRules"/> reads it.</summary>
public readonly record struct EmoteInput(
    float Happiness, bool Sleeping, bool Danger, bool Party, bool Raining, bool LovedNear, bool HatedNear, bool NearOther,
    bool TalkingToPlayer, bool Pet);

/// <summary>
/// Picks an emote from a resident's mood, neighbours, events and the time of day (X-05). Pure: the caller supplies a
/// roll in [0, 1) so the choice is deterministic. Null = nothing worth showing.
/// </summary>
public static class EmoteRules
{
    public static string? Pick(in EmoteInput i, float roll)
    {
        if (i.Pet) return i.Sleeping ? "sleepy" : roll < 0.5f ? "love" : "happy";
        if (i.Danger) return roll < 0.75f ? "alarm" : "sad";
        if (i.Sleeping) return "sleepy";
        if (i.TalkingToPlayer) return i.Happiness >= 1.1f ? "sad" : i.Happiness <= 0.95f ? roll < 0.5f ? "happy" : "laugh" : "chat";
        if (i.Party) return roll < 0.6f ? "music" : "laugh";
        if (i.HatedNear && roll < 0.7f) return "angry";
        if (i.LovedNear && roll < 0.7f) return "love";
        if (i.Happiness >= 1.1f) return roll < 0.5f ? "angry" : "sad";
        if (i.Raining && roll < 0.4f) return "sad";
        if (i.NearOther) return roll < 0.7f ? "chat" : "laugh";
        if (i.Happiness <= 0.9f) return roll < 0.5f ? "happy" : "music";
        return roll < 0.3f ? "question" : roll < 0.45f ? "idea" : null;
    }

    /// <summary>How a resident answers an emote from the player or a neighbour.</summary>
    public static string Reply(string emote, float happiness) => emote switch
    {
        "love" => happiness >= 1.1f ? "question" : "love",
        "angry" => "sad",
        "sad" => "love",
        "question" => "idea",
        "chat" => "laugh",
        "sleepy" => "sleepy",
        "music" => "music",
        "alarm" => "alarm",
        _ => happiness >= 1.1f ? "sad" : "happy",
    };
}
