namespace Strata.Core.Content.Defs;

/// <summary>A hostile or passive creature. Behaviour comes from its <see cref="Brain"/> spec.</summary>
public sealed class EnemyDef : ContentDef
{
    /// <summary>AI brain spec, e.g. { "type": "slimeHop", "jumpHeight": 5 }.</summary>
    public TypedSpec Brain { get; init; } = TypedSpec.Of("idle");

    public int Life { get; init; } = 10;
    public int Damage { get; init; }
    public int Defense { get; init; }

    /// <summary>0 = full knockback, 1 = immune.</summary>
    public float KnockbackResist { get; init; }

    /// <summary>Collision size [width, height] in cells.</summary>
    public float[] Size { get; init; } = { 2f, 2f };

    public float MoveSpeed { get; init; } = 3f;

    public bool Gravity { get; init; } = true;
    public bool CollideTerrain { get; init; } = true;
    public bool ContactDamage { get; init; } = true;

    /// <summary>Creature model name (assets/models/creatures/&lt;model&gt;.glb); several enemies may share one.</summary>
    public string? Model { get; init; }

    /// <summary>Colour multiplier applied to the model (recolours shared models such as gels; white = as modelled).</summary>
    public string Tint { get; init; } = "#ffffff";
    public float Scale { get; init; } = 1f;

    public string? Loot { get; init; }

    /// <summary>Coins dropped (copper).</summary>
    public int Value { get; init; }

    public string? HitSound { get; init; }
    public string? DeathSound { get; init; }

    /// <summary>Emitted light [r,g,b] 0–15 (glowing creatures).</summary>
    public int[]? Light { get; init; }

    public float Width => Size.Length > 0 ? Size[0] : 2f;
    public float Height => Size.Length > 1 ? Size[1] : 2f;
}

/// <summary>Where and when an enemy may naturally spawn.</summary>
public sealed class SpawnRuleDef : ContentDef
{
    public string Enemy { get; init; } = "";
    public float Weight { get; init; } = 1f;
    public int[] Group { get; init; } = { 1, 1 };
    public TypedSpec[] Conditions { get; init; } = Array.Empty<TypedSpec>();

    public int GroupMin => Group.Length > 0 ? Group[0] : 1;
    public int GroupMax => Group.Length > 1 ? Group[1] : GroupMin;
}
