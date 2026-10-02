namespace Strata.Core.Content.Specs;

/// <summary>Parameters for projectile on-hit effects.</summary>
public static class ProjectileHitSpecs
{
    public const string Explode = "explode";
    public const string ApplyBuff = "applyBuff";
    public const string LifeSteal = "lifeSteal";
    public const string SpawnProjectiles = "spawnProjectiles";
    public const string DropItem = "dropItem";

    public static void Register(SpecCatalog c)
    {
        c.Register<ExplodeParams>(SpecKind.ProjectileHit, Explode);
        c.Register<ApplyBuffParams>(SpecKind.ProjectileHit, ApplyBuff);
        c.Register<LifeStealParams>(SpecKind.ProjectileHit, LifeSteal);
        c.Register<SpawnProjectilesParams>(SpecKind.ProjectileHit, SpawnProjectiles);
        c.Register<DropItemParams>(SpecKind.ProjectileHit, DropItem);
    }
}

public sealed class ExplodeParams : SpecParams
{
    /// <summary>Blast radius in cells.</summary>
    public float Radius { get; init; } = 3f;

    public int Damage { get; init; }

    /// <summary>Destroys terrain within the radius (respecting pickaxe power).</summary>
    public bool DestroyTerrain { get; init; }

    public int PickPower { get; init; } = 0;

    /// <summary>Also hurts players in the blast (the thrower included), as bombs and dynamite do.</summary>
    public bool HurtsPlayers { get; init; }
}

public sealed class ApplyBuffParams : SpecParams
{
    public string Buff { get; init; } = "";
    public int Ticks { get; init; } = 180;
    public float Chance { get; init; } = 1f;
}

/// <summary>Heals the projectile's owner by a share of the damage it dealt to a creature (Vampire Knives).</summary>
public sealed class LifeStealParams : SpecParams, IValidatableSpec
{
    public float Fraction { get; init; } = 0.075f;

    public string? Validate() => Fraction is <= 0 or > 1 ? "fraction must be in (0, 1]" : null;
}

/// <summary>Releases more projectiles where it strikes a creature (the Scourge of the Corruptor's tiny eaters).</summary>
public sealed class SpawnProjectilesParams : SpecParams, IValidatableSpec
{
    public string Projectile { get; init; } = "";
    public int Count { get; init; } = 3;

    /// <summary>Launch speed in m/s.</summary>
    public float Speed { get; init; } = 10f;

    /// <summary>Launched at the nearest other creatures (true) or sprayed in random directions (false).</summary>
    public bool Homing { get; init; }

    /// <summary>Their damage as a share of the parent projectile's.</summary>
    public float DamageScale { get; init; } = 0.5f;

    public string? Validate() =>
        string.IsNullOrEmpty(Projectile) ? "projectile is required"
        : Count < 1 ? "count must be >= 1"
        : DamageScale < 0 ? "damageScale must be >= 0" : null;
}

/// <summary>Leaves an item where the projectile comes to rest on the ground (a Fallen Star landing, P3-30).</summary>
public sealed class DropItemParams : SpecParams, IValidatableSpec
{
    public string Item { get; init; } = "";
    public int Count { get; init; } = 1;

    public string? Validate() => string.IsNullOrEmpty(Item) ? "item is required" : Count < 1 ? "count must be >= 1" : null;
}
