using Strata.Core.Content.Defs;

namespace Strata.Core.Gameplay.Combat;

/// <summary>What hurt a player, for the death message (data/config/deaths.json has lines for each kind).</summary>
public enum HarmKind { Enemy, Projectile, Fall, Lava, Drowning, Debuff, Trap, Other }

/// <summary>
/// The last thing that hurt a player (<see cref="Session.PlayerEntity.LastHarm"/>): its kind, the phrase naming the
/// source as the death message reads it ("a Zombie", "King Slime", "On Fire!") and, for a debuff, the buff's id (some
/// have lines of their own).
/// </summary>
public readonly record struct Harm(HarmKind Kind, string? Source = null, string? Id = null)
{
    public static readonly Harm Fall = new(HarmKind.Fall);
    public static readonly Harm Lava = new(HarmKind.Lava);
    public static readonly Harm Drowning = new(HarmKind.Drowning);
    public static readonly Harm Other = new(HarmKind.Other);

    /// <summary>A creature's touch (or its wall, its shots): a boss by name ("King Slime"), anything else "a Zombie".</summary>
    public static Harm By(EnemyDef enemy, HarmKind kind = HarmKind.Enemy) =>
        new(kind, enemy.Boss is { Minor: false } ? enemy.DisplayName : WithArticle(enemy.DisplayName), enemy.Id);

    /// <summary>A shot with no creature behind it (a dart trap's dart, the player's own bomb): "a Poison Dart".</summary>
    public static Harm By(ProjectileDef projectile) => new(HarmKind.Projectile, WithArticle(projectile.DisplayName), projectile.Id);

    /// <summary>A damage-over-time debuff ("On Fire!").</summary>
    public static Harm By(BuffDef buff) => new(HarmKind.Debuff, buff.DisplayName, buff.Id);

    /// <summary>"a Zombie", "an Antlion"; "a Unicorn" (a "you" sound takes "a").</summary>
    public static string WithArticle(string noun)
    {
        if (noun.Length == 0) return noun;
        bool vowel = "AEIOUaeiou".IndexOf(noun[0]) >= 0 && !noun.StartsWith("Uni", StringComparison.OrdinalIgnoreCase)
            && !noun.StartsWith("Use", StringComparison.OrdinalIgnoreCase) && !noun.StartsWith("Eu", StringComparison.OrdinalIgnoreCase);
        return (vowel ? "an " : "a ") + noun;
    }
}
