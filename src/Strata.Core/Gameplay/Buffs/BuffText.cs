using System.Globalization;
using Strata.Core.Content;
using Strata.Core.Content.Defs;
using Strata.Core.Content.Specs;
using Strata.Core.Gameplay.Stats;

namespace Strata.Core.Gameplay.Buffs;

/// <summary>
/// Turns a buff's effects into Terraria's tooltip wording ("+8 defense", "25% increased movement speed", "Slowly
/// losing life", "Immune to Chilled and Frozen") for buffs whose data carries no description of its own.
/// </summary>
public static class BuffText
{
    private enum Unit
    {
        /// <summary>A fraction shown as a percentage (0.25 → 25%).</summary>
        Percent,

        /// <summary>Already in percentage points (critical strike chance).</summary>
        Points,

        /// <summary>A plain amount (defense, life, cells).</summary>
        Flat,
    }

    /// <summary>How one stat reads: {0} is the magnitude; Up is used for gains, Down for losses.</summary>
    private sealed record Wording(Unit Unit, string Noun, string Up, string Down);

    private static Wording Pct(string noun) => new(Unit.Percent, noun, "{0}% increased " + noun, "{0}% reduced " + noun);

    private static Wording Crit(string noun) => new(Unit.Points, noun, "{0}% increased " + noun, "{0}% reduced " + noun);

    private static Wording Flat(string noun) => new(Unit.Flat, noun, "+{0} " + noun, "-{0} " + noun);

    private static readonly Dictionary<string, Wording> StatWords = new(StringComparer.Ordinal)
    {
        [StatIds.MaxLife] = Flat("maximum life"),
        [StatIds.MaxMana] = Flat("maximum mana"),
        [StatIds.Defense] = Flat("defense"),
        [StatIds.LifeRegen] = new(Unit.Flat, "life regeneration", "Provides life regeneration", "Reduced life regeneration"),
        [StatIds.ManaRegen] = new(Unit.Flat, "mana regeneration", "Increased mana regeneration", "Reduced mana regeneration"),
        [StatIds.MoveSpeed] = Pct("movement speed"),
        [StatIds.Acceleration] = Pct("acceleration"),
        [StatIds.JumpHeight] = Pct("jump height"),
        [StatIds.SwimSpeed] = Pct("swimming speed"),
        [StatIds.GenericDamage] = Pct("damage"),
        [StatIds.MeleeDamage] = Pct("melee damage"),
        [StatIds.RangedDamage] = Pct("ranged damage"),
        [StatIds.MagicDamage] = Pct("magic damage"),
        [StatIds.SummonDamage] = Pct("summon damage"),
        [StatIds.GenericCrit] = Crit("critical strike chance"),
        [StatIds.MeleeCrit] = Crit("melee critical strike chance"),
        [StatIds.RangedCrit] = Crit("ranged critical strike chance"),
        [StatIds.MagicCrit] = Crit("magic critical strike chance"),
        [StatIds.MeleeSpeed] = Pct("melee speed"),
        [StatIds.MiningSpeed] = Pct("mining speed"),
        [StatIds.KnockbackResist] = Pct("knockback resistance"),
        [StatIds.ManaCost] = Pct("mana cost"),
        [StatIds.MaxMinions] = new(Unit.Flat, "max number of minions", "Increases your max number of minions by {0}", "Decreases your max number of minions by {0}"),
        [StatIds.Reach] = new(Unit.Flat, "range", "+{0} block range", "-{0} block range"),
        [StatIds.EnduranceReduction] = new(Unit.Percent, "damage taken", "{0}% reduced damage taken", "{0}% increased damage taken"),
        [StatIds.FlightTime] = new(Unit.Flat, "flight time", "Increased flight time", "Reduced flight time"),
        [StatIds.Immunity] = new(Unit.Flat, "invincibility", "Longer invincibility after taking damage", "Shorter invincibility after taking damage"),
        [StatIds.AmmoSave] = new(Unit.Percent, "ammo use", "{0}% chance not to consume ammo", "{0}% more ammo used"),
        [StatIds.Thorns] = new(Unit.Percent, "thorns", "Attackers also take {0}% of the damage they deal", "Reduced thorns"),
        [StatIds.MeleeKnockback] = Pct("melee knockback"),
        [StatIds.PotionDelay] = new(Unit.Percent, "potion cooldown", "{0}% longer potion cooldown", "Reduces the cooldown of healing potions by {0}%"),
        [StatIds.LifeSteal] = new(Unit.Percent, "life steal", "{0}% of magic damage dealt heals you", "Reduced life steal"),
    };

    private static readonly Dictionary<string, string> InfoWords = new(StringComparer.Ordinal)
    {
        ["depth"] = "Displays depth",
        ["position"] = "Displays position",
        ["time"] = "Displays the time",
        ["ores"] = "Shows the location of treasure and ore",
        ["night_vision"] = "Increased night vision",
    };

    /// <summary>The buff's tooltip text: its own description, else wording derived from its effects (null if neither).</summary>
    public static string? Describe(BuffDef buff, ContentDatabase? content = null) =>
        !string.IsNullOrWhiteSpace(buff.Description) ? buff.Description : FromEffects(buff, content);

    /// <summary>Wording derived from the effects alone, one line per effect ('\n'-separated); null if there are none.</summary>
    public static string? FromEffects(BuffDef buff, ContentDatabase? content = null)
    {
        var lines = new List<string>();
        var immune = new List<string>();
        foreach (var e in buff.Effects)
        {
            switch (e.Type)
            {
                case EffectSpecs.Stat:
                    if (StatLine(e.Parse<StatEffectParams>()) is { } line) lines.Add(line);
                    break;
                case EffectSpecs.DamageOverTime: Add(lines, "Slowly losing life"); break;
                case EffectSpecs.EmitLight: Add(lines, "Emitting light"); break;
                case EffectSpecs.Immune: immune.Add(BuffName(e.Parse<ImmuneParams>().Buff, content)); break;
                case EffectSpecs.LiquidImmune: immune.Add(LiquidName(e.Parse<LiquidImmuneParams>().Liquid, content)); break;
                case EffectSpecs.NoFallDamage: immune.Add("fall damage"); break;
                case EffectSpecs.WaterBreathing: Add(lines, "Breathe water instead of air"); break;
                case EffectSpecs.NoItemUse: Add(lines, "Cannot use items"); break;
                case EffectSpecs.Phase: Add(lines, "Passing through blocks"); break;
                case EffectSpecs.AutoPaint: Add(lines, "Paints blocks as they are placed"); break;
                case EffectSpecs.ExtraJump: Add(lines, "Allows an extra jump"); break;
                case EffectSpecs.Flight: Add(lines, "Allows flight"); break;
                case EffectSpecs.Dash: Add(lines, "Allows the player to dash"); break;
                case EffectSpecs.Pacify: Add(lines, $"{Humanize(e.Parse<PacifyParams>().Tag)}s become friendly"); break;
                case EffectSpecs.Inflict: Add(lines, $"Attacks inflict {BuffName(e.Parse<InflictParams>().Buff, content)}"); break;
                case EffectSpecs.Info:
                    foreach (var k in e.Parse<InfoParams>().Show)
                        if (InfoWords.TryGetValue(k, out var w)) Add(lines, w);
                    break;
            }
        }
        if (immune.Count > 0) lines.Add("Immune to " + JoinAnd(immune.Distinct().ToList()));
        return lines.Count == 0 ? null : string.Join('\n', lines);
    }

    /// <summary>One stat change in Terraria's wording; null for unknown stats or no change.</summary>
    private static string? StatLine(StatEffectParams s)
    {
        if (!StatWords.TryGetValue(s.Stat, out var w)) return null;
        if (s.Mul != 1f)
        {
            if (s.Mul <= 0f)
                return s.Stat switch
                {
                    StatIds.LifeRegen => "Cannot regenerate life",
                    StatIds.ManaRegen => "Cannot regenerate mana",
                    StatIds.MoveSpeed => "Cannot move",
                    _ => "No " + w.Noun,
                };
            int pct = (int)MathF.Round((s.Mul - 1f) * 100f);
            if (pct != 0) return pct > 0 ? $"{pct}% increased {w.Noun}" : $"{-pct}% reduced {w.Noun}";
        }
        if (s.Add == 0f) return null;
        float magnitude = MathF.Abs(s.Add) * (w.Unit == Unit.Percent ? 100f : 1f);
        return string.Format(CultureInfo.InvariantCulture, s.Add > 0 ? w.Up : w.Down, Number(magnitude));
    }

    private static string Number(float v) =>
        MathF.Abs(v - MathF.Round(v)) < 0.01f ? ((int)MathF.Round(v)).ToString(CultureInfo.InvariantCulture) : v.ToString("0.##", CultureInfo.InvariantCulture);

    private static void Add(List<string> lines, string line)
    {
        if (!lines.Contains(line)) lines.Add(line);
    }

    private static string BuffName(string id, ContentDatabase? content) =>
        content != null && content.Buffs.TryGet(id, out var b) ? b.DisplayName : Humanize(id);

    private static string LiquidName(string id, ContentDatabase? content) =>
        content != null && content.Liquids.TryGet(id, out var l) ? l.DisplayName.ToLowerInvariant() : id.Replace('_', ' ');

    /// <summary>"A", "A and B", "A, B and C".</summary>
    private static string JoinAnd(IReadOnlyList<string> parts) =>
        parts.Count == 1 ? parts[0] : string.Join(", ", parts.Take(parts.Count - 1)) + " and " + parts[^1];

    /// <summary>A snake_case id as words with a capital ("on_fire" → "On fire").</summary>
    internal static string Humanize(string id)
    {
        var words = id.Replace('_', ' ').Trim();
        return words.Length == 0 ? words : char.ToUpperInvariant(words[0]) + words[1..];
    }
}
