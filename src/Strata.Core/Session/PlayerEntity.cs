using System.Numerics;
using Strata.Core.Common;
using Strata.Core.Gameplay.Player;

namespace Strata.Core.Session;

/// <summary>A player character inside a world: persistent state plus its live body (synced by the engine layer).</summary>
public sealed class PlayerEntity : Gameplay.Combat.ICombatTarget
{
    public PlayerEntity(PlayerState state) => State = state;

    public PlayerState State { get; }

    /// <summary>Feet position in meters.</summary>
    public Vector3 Position { get; set; }

    public Vector3 Velocity { get; set; }

    /// <summary>Normalized aim direction: from the eye to the crosshair point (shots, swing pitch, item drops).</summary>
    public Vector3 Aim { get; set; } = -Vector3.UnitZ;

    /// <summary>
    /// The camera's forward (normalized), set by the engine each tick and by the <c>aim</c> command; null while nothing
    /// has set it (headless tests that only aim).
    /// </summary>
    public Vector3? ViewDirection { get; set; }

    /// <summary>
    /// Where the player is looking: the view direction when known, else the aim. Unlike <see cref="Aim"/> it does not
    /// swing towards close targets beside the shoulder camera, so it drives the build rotation and the melee arc.
    /// </summary>
    public Vector3 Facing => ViewDirection ?? Aim;

    /// <summary>Distance (m) from the eye to the crosshair point along <see cref="Aim"/> (terrain, or far away).</summary>
    public float AimDistance { get; set; } = 40f;

    /// <summary>
    /// Ticks until the held item acts again (Terraria's item time). Inside one animation a tool digs, and a gun whose
    /// use time is shorter than its animation fires, each time this runs out.
    /// </summary>
    public int UseCooldown { get; set; }

    /// <summary>Ticks before equipment may throw its next attack projectile (Bone Glove).</summary>
    public int GloveCooldown { get; set; }

    /// <summary>Ticks left in the current use animation (swing/shoot pose); a new animation starts only once it is 0.</summary>
    public int UseAnimation { get; set; }

    /// <summary>Length (ticks) of the current use animation after speed bonuses; <see cref="UseAnimation"/> counts down from it.</summary>
    public int UseAnimationMax { get; set; }

    /// <summary>Goes up by one whenever a new use animation starts: presentation plays each clip once per change.</summary>
    public int UseSerial { get; set; }

    /// <summary>Ticks before the next use animation may start (the item's reuse delay, counted once its animation ends).</summary>
    public int ReuseDelay { get; set; }

    /// <summary>The item whose animation is (or was last) playing; using another item starts a new animation.</summary>
    public Content.Defs.ItemDef? UseItem { get; set; }

    /// <summary>Ends the current use so the next one starts a fresh animation at once (scripted uses, tests).</summary>
    public void ResetUse()
    {
        UseCooldown = 0;
        UseAnimation = 0;
        ReuseDelay = 0;
    }

    /// <summary>The grappling hook: thrown, latched or returning (see <see cref="GrappleHook"/>).</summary>
    public GrappleHook Hook { get; } = new();

    /// <summary>
    /// A velocity (m/s) a creature forces on the player this tick, replacing their own movement (the Wall of Flesh's
    /// tongue); zero when nothing drags them. The creature system clears it each tick; the body (or, headless,
    /// <see cref="GameWorld"/>) applies it.
    /// </summary>
    public Vector3 ExternalVelocity { get; set; }

    /// <summary>
    /// An engine body owns this player's motion (the running game): it advances <see cref="Hook"/> in its physics step
    /// and applies the pull. Without one (tests, headless runs) <see cref="GameWorld"/> advances the hook each tick.
    /// </summary>
    public bool HasBody { get; set; }

    /// <summary>
    /// The throwing hand (m): chest height, a little to the right of and ahead of the body along the horizontal
    /// <see cref="Facing"/> (straight up or down counts as facing −Z). Hooks leave and return to it.
    /// </summary>
    public Vector3 Hand
    {
        get
        {
            var f = Facing;
            var flat = new Vector3(f.X, 0, f.Z);
            flat = flat.LengthSquared() > 1e-6f ? Vector3.Normalize(flat) : -Vector3.UnitZ;
            var right = new Vector3(-flat.Z, 0, flat.X);
            return Position + new Vector3(0, 1.25f, 0) + right * 0.28f + flat * 0.1f;
        }
    }

    /// <summary>Crisp shape used when placing blocks.</summary>
    public Voxels.CellShape BuildShape { get; set; } = Voxels.CellShape.Full;

    /// <summary>Quarter turns about +Y for shapes and objects (added to the facing direction).</summary>
    public int BuildRotation { get; set; }

    /// <summary>The minecart being ridden (the engine moves the body along it), or null.</summary>
    public Gameplay.Mounts.MinecartRide? Minecart { get; set; }

    /// <summary>Pressure plate currently under the feet (signals once per step onto it).</summary>
    public long PressurePlate { get; set; } = -1;

    /// <summary>Ticks until respawn while dead.</summary>
    public int RespawnTicks { get; set; }

    /// <summary>A hardcore character that died: it never respawns and is not saved again.</summary>
    public bool Lost { get; set; }

    /// <summary>Precision mode: tools affect a single cell.</summary>
    public bool Precision { get; set; }

    /// <summary>Liquid the body is standing or swimming in (null when dry). Set by the liquid effects each tick.</summary>
    public Content.Defs.LiquidDef? Liquid { get; set; }

    /// <summary>The head is under the liquid's surface.</summary>
    public bool Submerged { get; set; }

    public const int MaxBreath = 1200;

    /// <summary>Ticks of air left while submerged (refills in the open).</summary>
    public int Breath { get; set; } = MaxBreath;

    public static readonly Vector3 HalfExtents = new(0.45f, 0.9f, 0.45f);

    /// <summary>
    /// The cell containing the feet. Bodies rest a few centimetres into the smoothed ground surface, so the cell is
    /// taken slightly above the feet point.
    /// </summary>
    public Common.Int3 FeetCell => new(
        (int)MathF.Floor(Position.X / Units.CellSize),
        (int)MathF.Floor((Position.Y + 0.1f) / Units.CellSize),
        (int)MathF.Floor(Position.Z / Units.CellSize));

    /// <summary>Eye position in meters.</summary>
    public Vector3 Eye => Position + new Vector3(0, 1.6f, 0);

    /// <summary>
    /// Horizontal <see cref="Facing"/> as a <see cref="Voxels.ShapeCode"/> rotation: 0 = looking +Z, 1 = −X, 2 = −Z, 3 = +X.
    /// Slopes placed with it rise away from the player; objects face the player.
    /// </summary>
    public int FacingQuarter
    {
        get
        {
            var f = Facing;
            int q = (int)MathF.Round(MathF.Atan2(f.X, f.Z) / (MathF.PI / 2f));
            return ((-q % 4) + 4) % 4;
        }
    }

    public Vector3 Center => Position + new Vector3(0, HalfExtents.Y, 0);

    public bool Alive => State.Vitals.Life > 0;

    /// <summary>Box in cell units (for placement blocking).</summary>
    public (Vector3 Min, Vector3 Max) CellBox => ((Center - HalfExtents) / Units.CellSize, (Center + HalfExtents) / Units.CellSize);
}
