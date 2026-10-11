using System.Text.Json;
using Strata.Core.Content;
using Strata.Core.Content.Loading;

namespace Strata.Core.Gameplay.Items;

/// <summary>
/// Heart and Star drops and item magnetism (data/config/pickups.json; defaults are Terraria 1.4.4's
/// NPCLoot_DropHeals and NPCLoot_DropBossPotionsAndHearts).
/// </summary>
public sealed class PickupConfig
{
    public const string Path = "data/config/pickups.json";

    /// <summary>One kill in this many rolls for a pickup (Terraria: 1 in 6).</summary>
    public int DropOneIn { get; set; } = 6;

    /// <summary>Within that roll: a Star when the killer's mana is not full (1/2)…</summary>
    public float StarChance { get; set; } = 0.5f;

    /// <summary>…otherwise a Heart when the killer's life is not full (1/2).</summary>
    public float HeartChance { get; set; } = 0.5f;

    /// <summary>A kill whose last hit was magic rolls an extra Star at this chance when the killer's mana is not full.</summary>
    public float MagicStarChance { get; set; } = 0.5f;

    /// <summary>Hearts spilled by a boss defeat, inclusive range (Terraria: 5 to 9).</summary>
    public int[] BossHearts { get; set; } = { 5, 9 };

    /// <summary>Distance (m) at which a dropped item starts flying to its collector; the grabRange stat adds to it.</summary>
    public float MagnetRange { get; set; } = DroppedItemSystem.MagnetRange;

    public float PickupRange { get; set; } = DroppedItemSystem.PickupRange;

    /// <summary>Speed (m/s) a magnetised item flies at.</summary>
    public float PullSpeed { get; set; } = 9f;

    public static PickupConfig Load(IDataSource data) =>
        JsonSerializer.Deserialize<PickupConfig>(data.ReadText(Path), ContentJson.Options) ?? throw new JsonException($"{Path} is empty");
}
