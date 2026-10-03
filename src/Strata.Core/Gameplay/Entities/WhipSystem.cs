using System.Numerics;
using Strata.Core.Content.Defs;
using Strata.Core.Content.Specs;
using Strata.Core.Gameplay.Items;
using Strata.Core.Session;

namespace Strata.Core.Gameplay.Entities;

/// <summary>
/// Whips (P3-14): a lash follows <see cref="WhipGeometry"/> over the use animation and, while it can hit, strikes every
/// creature its points touch, each once per crack, for less damage per creature already hit (×(1 − falloff)^hits). A
/// struck creature takes the whip's tag debuff (a <c>minionTag</c> effect: the owner's minion hits deal more) and is
/// marked as the owner's target, so their minions turn on it; Snapthorn also poisons.
/// </summary>
public sealed class WhipSystem
{
    private const float HitRadius = 0.3f;

    public sealed class Lash
    {
        public required PlayerEntity Owner { get; init; }
        public required ItemUseInfo Weapon { get; init; }
        public required int Damage { get; init; }
        public required int Crit { get; init; }
        public required float Knockback { get; init; }
        public required int Duration { get; init; }
        public int Age;
        public int Hits;
        public readonly HashSet<long> Struck = new();

        /// <summary>Use fraction 0–1.</summary>
        public float T => Duration > 0 ? (float)Age / Duration : 1f;
    }

    private readonly GameWorld _world;
    private readonly List<Lash> _lashes = new();
    private readonly Vector3[] _points = new Vector3[64];

    public WhipSystem(GameWorld world) => _world = world;

    /// <summary>Lashes in the air (the renderer draws them).</summary>
    public IReadOnlyList<Lash> Active => _lashes;

    /// <summary>Creatures struck by lashes so far (probe <c>whips.hits</c>).</summary>
    public int TotalHits { get; private set; }

    /// <summary>Starts a lash for <paramref name="p"/>'s held whip, replacing one still in the air.</summary>
    public void Crack(PlayerEntity p, ItemUseInfo weapon, int durationTicks)
    {
        _lashes.RemoveAll(l => l.Owner == p);
        var held = p.State.Inventory.Held.Item == weapon.Item ? p.State.Inventory.Held : new ItemStack(weapon.Item, 1);
        _lashes.Add(new Lash
        {
            Owner = p, Weapon = weapon, Damage = Prefixes.Damage(held), Crit = Prefixes.Crit(held) + Combat.DamageModel.BaseCritChance,
            Knockback = Prefixes.Knockback(held), Duration = Math.Max(1, durationTicks),
        });
    }

    /// <summary>The lash's points right now (hand to tip), into a shared buffer.</summary>
    public ReadOnlySpan<Vector3> PointsOf(Lash lash)
    {
        var w = lash.Weapon.Whip!;
        var span = _points.AsSpan(0, Math.Min(w.Segments, _points.Length));
        var p = lash.Owner;
        var forward = p.Aim.LengthSquared() > 1e-6f ? Vector3.Normalize(p.Aim) : Vector3.UnitZ;
        var side = Vector3.Cross(forward, Vector3.UnitY);
        var up = side.LengthSquared() > 1e-6f ? Vector3.Normalize(Vector3.Cross(Vector3.Normalize(side), forward)) : Vector3.UnitY;
        var hand = ShotGeometry.Origin(_world.Session.World, p);
        WhipGeometry.Points(lash.T, hand, forward, up, w.Length * Common.Units.CellSize, span);
        return span;
    }

    public void Tick()
    {
        for (int i = _lashes.Count - 1; i >= 0; i--)
        {
            var lash = _lashes[i];
            lash.Age++;
            if (!lash.Owner.Alive || lash.Age > lash.Duration)
            {
                _lashes.RemoveAt(i);
                continue;
            }
            float t = lash.T;
            if (t >= WhipGeometry.HitStart && t <= WhipGeometry.HitEnd) Strike(lash);
        }
    }

    private void Strike(Lash lash)
    {
        var points = PointsOf(lash);
        var w = lash.Weapon.Whip!;
        var r = new Vector3(HitRadius);
        foreach (var npc in _world.Npcs.All)
        {
            if (!npc.Alive || npc.Invulnerable || lash.Struck.Contains(npc.Id) || npc.Def.HasTag("critter")) continue;
            bool touched = false;
            for (int k = 1; k < points.Length && !touched; k++)
            {
                var box = (points[k] - r, points[k] + r);
                for (int v = 0; v < npc.VolumeCount && !touched; v++) touched = Npc.Overlaps(npc.VolumeAt(v), box);
            }
            if (!touched) continue;
            lash.Struck.Add(npc.Id);
            int damage = Math.Max(1, (int)MathF.Round(lash.Damage * MathF.Pow(1f - w.Falloff, lash.Hits)));
            lash.Hits++;
            TotalHits++;
            if (!_world.Npcs.Hit(npc, lash.Owner, damage, DamageClass.Summon, lash.Crit, lash.Knockback, lash.Owner.Center, melee: false)) continue;
            if (!npc.Alive) continue;
            npc.TaggedBy = lash.Owner;
            npc.TagTick = _world.Session.Clock.Tick;
            var buffs = _world.Session.Content.Buffs;
            if (w.TagBuff != null && buffs.TryGet(w.TagBuff, out var tag)) _world.Npcs.AddBuff(npc, tag, w.TagTicks);
            if (w.Inflict != null && buffs.TryGet(w.Inflict, out var inflict)) _world.Npcs.AddBuff(npc, inflict, w.InflictTicks);
        }
    }
}

[Commands.ProbeSource]
internal static class WhipProbes
{
    public static void Register(Commands.CommandContext ctx) => ctx.AddProbe("whips.hits", () => ctx.World.Whips.TotalHits);
}
