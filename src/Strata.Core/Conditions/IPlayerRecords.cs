namespace Strata.Core.Conditions;

/// <summary>Journal facts about the players that conditions may ask about (implemented by the player journal).</summary>
public interface IPlayerRecords
{
    int Kills(string enemy);

    bool Obtained(string item);

    /// <summary>Bestiary completion, 0–100.</summary>
    float BestiaryPercent { get; }

    bool Achieved(string id);
}
