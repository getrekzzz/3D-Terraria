using System.Numerics;
using Strata.Core.Common;
using Strata.Core.Content.Defs;
using Strata.Core.Determinism;
using Strata.Core.Gameplay.Entities;
using Strata.Core.Physics;
using Strata.Core.Session;
using Strata.Core.Voxels;
using Strata.Core.World;

namespace Strata.Core.Gameplay.Events;

/// <summary>
/// Fallen Stars (P3-30): at night, once a second for each living player on the surface, a star may streak down from
/// the sky to land within <see cref="StarfallConfig.Radius"/> of them, where it lies as a Fallen Star item. Stars only
/// start over loaded chunks, at most <see cref="StarfallConfig.MaxActive"/> at a time. When day breaks, every
/// <see cref="ItemDef.FadesAtDawn"/> item still on the ground vanishes.
/// </summary>
public sealed class StarfallDirector
{
    private readonly GameWorld _world;
    private readonly ulong _seed;
    private bool _wasDay;

    public StarfallDirector(GameWorld world)
    {
        _world = world;
        _seed = Hash.Combine((ulong)world.Session.Settings.Seed, 0x5354415246UL);
        _wasDay = world.Session.Clock.IsDay;
    }

    public StarfallConfig Config { get; set; } = new();

    /// <summary>Stars that have started falling this session (probe <c>stars.fallen</c>).</summary>
    public int Fallen { get; private set; }

    /// <summary>Fallen Stars faded at dawn this session (probe <c>stars.faded</c>).</summary>
    public int Faded { get; private set; }

    /// <summary>A star started falling (the engine plays its whoosh).</summary>
    public event Action<Projectile>? Started;

    public void Tick()
    {
        var clock = _world.Session.Clock;
        bool day = clock.IsDay;
        if (day && !_wasDay) Faded += _world.Drops.RemoveWhere(d => d.Stack.Item?.FadesAtDawn == true);
        _wasDay = day;
        if (day || clock.Tick % Units.TicksPerSecond != 0 || Config.ChancePerSecond <= 0) return;
        var rng = new Pcg32(Hash.Combine(_seed, (ulong)clock.Tick));
        foreach (var player in _world.Players)
            if (player.Alive && rng.Chance(Config.ChancePerSecond)) TryFall(player, ref rng);
    }

    /// <summary>Drops one star near <paramref name="player"/> now (the <c>starfall now</c> command); null if none could start.</summary>
    public Projectile? FallNear(PlayerEntity player)
    {
        var rng = new Pcg32(Hash.Combine(_seed, (ulong)_world.Session.Clock.Tick ^ 0x4E4F57UL));
        return TryFall(player, ref rng);
    }

    private Projectile? TryFall(PlayerEntity player, ref Pcg32 rng)
    {
        var session = _world.Session;
        if (session.Generator.Plan.Bands.LayerAt(player.FeetCell.Y) is not (WorldLayer.Surface or WorldLayer.Sky)) return null;
        if (!session.Content.Projectiles.TryGet(Config.Projectile, out var def)) return null;
        int active = 0;
        foreach (var p in _world.Projectiles.All)
            if (p.Def == def) active++;
        if (active >= Config.MaxActive) return null;

        // Where it lands (roughly: it falls on whatever is there) and where it starts, up and off to one side.
        float angle = rng.NextFloat() * MathF.Tau;
        float reach = Config.Radius * MathF.Sqrt(rng.NextFloat());
        var target = player.Position + new Vector3(MathF.Cos(angle), 0, MathF.Sin(angle)) * reach;
        var world = session.World;
        if (world.GetBlock(VoxelMotion.CellOf(target)) == VoxelWorld.Unknown) return null;
        float slant = rng.Range(0.35f, 0.7f); // horizontal metres per metre of fall
        float side = rng.NextFloat() * MathF.Tau;
        var across = new Vector3(MathF.Cos(side), 0, MathF.Sin(side));
        float height = StartHeight(target, across * slant);
        if (height < Config.MinHeight) return null;
        var start = target + new Vector3(0, height, 0) - across * (slant * height);
        var velocity = Vector3.Normalize(target - start) * Config.Speed;
        Fallen++;
        var star = _world.Projectiles.Spawn(def, start, velocity, null, def.Contact ? StarDamage : 0, 2f, DamageClass.Ranged, 0);
        Started?.Invoke(star);
        return star;
    }

    /// <summary>Damage a falling star deals to creatures it passes through (Terraria's is modest).</summary>
    public const int StarDamage = 25;

    /// <summary>How high above <paramref name="target"/> a star can start along its slant: the configured height, or
    /// lower where the sky above is not loaded or is blocked.</summary>
    private float StartHeight(Vector3 target, Vector3 slantPerMetre)
    {
        var world = _world.Session.World;
        float best = 0;
        for (float h = 1f; h <= Config.Height; h += 1f)
        {
            var cell = VoxelMotion.CellOf(target + new Vector3(0, h, 0) - slantPerMetre * h);
            ushort b = world.GetBlock(cell);
            if (b == VoxelWorld.Unknown) break;
            if (b != Content.ContentDatabase.Air && world.Content.Blocks[b].IsSolid) best = 0; // under a roof: start above it
            else best = h;
        }
        return best;
    }
}
