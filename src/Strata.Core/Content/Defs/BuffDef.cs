namespace Strata.Core.Content.Defs;

/// <summary>A timed status effect (buff or debuff) composed of effect specs.</summary>
public sealed class BuffDef : ContentDef
{
    public bool Debuff { get; init; }

    /// <summary>Icon image name under assets/icons/buffs (defaults to the id).</summary>
    public string? Icon { get; init; }
    public string Color { get; init; } = "#ffffff";
    public TypedSpec[] Effects { get; init; } = Array.Empty<TypedSpec>();

    /// <summary>Default duration in ticks when applied by a rule rather than an explicit source.</summary>
    public int Duration { get; init; } = 600;

    /// <summary>
    /// The food tier (Terraria's Well Fed 1, Plenty Satisfied 2, Exquisitely Stuffed 3; 0 for anything else). Food
    /// buffs replace each other, and quick buff eats nothing while any of them is on.
    /// </summary>
    public int FoodTier { get; init; }

    /// <summary>Removed on death / cannot be cancelled by the player.</summary>
    public bool Persistent { get; init; }

    /// <summary>
    /// The buff bar shows no countdown (aura and liquid buffs such as Cozy Fire, Heart Lamp and Honey, which are
    /// refreshed every few ticks while the source is near and would otherwise flicker).
    /// </summary>
    public bool HideTimer { get; init; }

    /// <summary>How the status shows on whoever carries it (body tint, particles, light); null = icon only.</summary>
    public BuffVisualDef? Visual { get; init; }
}

/// <summary>
/// A buff's look on the player and on creatures (StatusVisuals): a body tint multiplied into the model's instance
/// <c>tint</c>, a looping particle emitter from the EffectKit pool and an optional light from the dynamic pool.
/// </summary>
public sealed class BuffVisualDef
{
    /// <summary>Particle ids: the EffectKit recipes a status emitter can borrow its look from.</summary>
    public static readonly string[] Particles = { "chips", "dust", "sparks", "puff", "splash", "leaves" };

    /// <summary>Body tint #RRGGBB multiplied into the model (white = none).</summary>
    public string? Tint { get; init; }

    /// <summary>How strongly the tint applies (0–1); it gently pulses around this.</summary>
    public float TintStrength { get; init; } = 0.6f;

    /// <summary>Particle id (one of <see cref="Particles"/>), or null for a tint only.</summary>
    public string? Particle { get; init; }

    /// <summary>Particle colour #RRGGBB (defaults to the buff's colour).</summary>
    public string? Color { get; init; }

    /// <summary>Particles per second at effectsScale 1.</summary>
    public float Rate { get; init; } = 12f;

    /// <summary>Particle drift: positive rises (flames), negative drips (poison, water).</summary>
    public float Rise { get; init; } = 1f;

    /// <summary>Optional light [r, g, b] 0–15 from the dynamic light pool (at most the preset's statusLights).</summary>
    public int[]? Light { get; init; }
}
