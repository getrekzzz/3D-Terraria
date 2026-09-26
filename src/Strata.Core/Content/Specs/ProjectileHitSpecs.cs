namespace Strata.Core.Content.Specs;

/// <summary>Parameters for projectile on-hit effects.</summary>
public static class ProjectileHitSpecs
{
    public const string Explode = "explode";
    public const string ApplyBuff = "applyBuff";

    public static void Register(SpecCatalog c)
    {
        c.Register<ExplodeParams>(SpecKind.ProjectileHit, Explode);
        c.Register<ApplyBuffParams>(SpecKind.ProjectileHit, ApplyBuff);
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
