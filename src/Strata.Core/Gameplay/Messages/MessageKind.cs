namespace Strata.Core.Gameplay.Messages;

/// <summary>What a world announcement is about; the chat log colours it by kind (<see cref="MessageColors"/>).</summary>
public enum MessageKind
{
    Info,
    BossAwoken,
    BossDefeated,

    /// <summary>A boss changing phase or enraging, a pillar's shield falling.</summary>
    Boss,
    Event,
    Objective,

    /// <summary>Townsfolk news: an NPC rescued or moving in.</summary>
    Town,
}

/// <summary>Terraria's announcement colours per <see cref="MessageKind"/>, as #rrggbb.</summary>
public static class MessageColors
{
    /// <summary>Terraria's boss-message purple (175, 75, 255).</summary>
    public const string BossPurple = "#af4bff";

    public static string Hex(MessageKind kind) => kind switch
    {
        MessageKind.BossAwoken or MessageKind.BossDefeated or MessageKind.Boss => BossPurple,
        MessageKind.Event => BossPurple, // invasions and the Blood Moon use the same purple
        MessageKind.Objective => "#32ff82", // world-change messages (a meteorite, hardmode spirits) in green
        MessageKind.Town => "#327dff", // Terraria's NPC-arrival blue (50, 125, 255)
        _ => "#ffffff",
    };
}
