namespace Strata.Core.Content.Specs;

/// <summary>Parameters for item use behaviours (executed by the game layer's behaviour registry).</summary>
public static class ItemBehaviorSpecs
{
    public const string MeleeSwing = "meleeSwing";
    public const string Thrust = "thrust";
    public const string ShootProjectile = "shootProjectile";
    public const string MineTool = "mineTool";
    public const string ChopTool = "chopTool";
    public const string HammerTool = "hammerTool";
    public const string PlaceBlock = "placeBlock";
    public const string PlaceObject = "placeObject";
    public const string Consume = "consume";
    public const string SummonBoss = "summonBoss";

    public static void Register(SpecCatalog c)
    {
        c.Register<MeleeSwingParams>(SpecKind.ItemBehavior, MeleeSwing);
        c.Register<ThrustParams>(SpecKind.ItemBehavior, Thrust);
        c.Register<ShootProjectileParams>(SpecKind.ItemBehavior, ShootProjectile);
        c.Register<ToolParams>(SpecKind.ItemBehavior, MineTool);
        c.Register<ToolParams>(SpecKind.ItemBehavior, ChopTool);
        c.Register<ToolParams>(SpecKind.ItemBehavior, HammerTool);
        c.Register<PlaceParams>(SpecKind.ItemBehavior, PlaceBlock);
        c.Register<PlaceParams>(SpecKind.ItemBehavior, PlaceObject);
        c.Register<ConsumeParams>(SpecKind.ItemBehavior, Consume);
        c.Register<SummonBossParams>(SpecKind.ItemBehavior, SummonBoss);
    }
}

public sealed class MeleeSwingParams : SpecParams, IValidatableSpec
{
    /// <summary>Horizontal swing arc in degrees.</summary>
    public float Arc { get; init; } = 150f;

    /// <summary>Blade reach in cells (hitbox length from the hand).</summary>
    public float Length { get; init; } = 3f;

    public string? TrailColor { get; init; }

    public string? Validate() => Arc is <= 0 or > 360 ? "arc must be in (0, 360]" : Length <= 0 ? "length must be > 0" : null;
}

public sealed class ThrustParams : SpecParams
{
    public float Length { get; init; } = 3f;
}

public sealed class ShootProjectileParams : SpecParams, IValidatableSpec
{
    public int Count { get; init; } = 1;
    public float Spread { get; init; }

    public string? Validate() => Count < 1 ? "count must be >= 1" : null;
}

public sealed class ToolParams : SpecParams
{
}

public sealed class PlaceParams : SpecParams
{
}

public sealed class ConsumeParams : SpecParams
{
    /// <summary>Permanently raises max life by this amount (life-crystal style), up to <see cref="LifeCap"/>.</summary>
    public int PermanentLife { get; init; }

    public int LifeCap { get; init; } = 400;

    /// <summary>Permanently raises max mana (mana-crystal style), up to <see cref="ManaCap"/>.</summary>
    public int PermanentMana { get; init; }

    public int ManaCap { get; init; } = 200;
}

/// <summary>Summons a boss near the player when its conditions hold (e.g. night, a biome).</summary>
public sealed class SummonBossParams : SpecParams
{
    public string Enemy { get; init; } = "";

    /// <summary>Conditions evaluated at the player's position.</summary>
    public TypedSpec[] Conditions { get; init; } = Array.Empty<TypedSpec>();

    /// <summary>Spawn distance from the player (m).</summary>
    public float Distance { get; init; } = 25f;

    /// <summary>Message shown when the conditions fail.</summary>
    public string? FailMessage { get; init; }
}
