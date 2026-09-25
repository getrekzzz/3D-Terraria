using System.Numerics;
using Strata.Core.AI;
using Strata.Core.Common;
using Strata.Core.Gameplay.Items;
using Strata.Core.Session;

namespace Strata.Core.Gameplay.Entities;

/// <summary>
/// Resolves melee swings and thrusts over their use animation: the blade sweeps a horizontal arc in front of the
/// player (or stabs along the aim) and each creature is hit at most once per swing.
/// </summary>
public sealed class MeleeSystem
{
    private sealed class Swing
    {
        public required PlayerEntity Player { get; init; }
        public required ItemUseInfo Item { get; init; }
        public required int Duration { get; init; }
        public required float Yaw { get; init; }
        public required float Pitch { get; init; }
        public int Tick;
        public readonly HashSet<long> Hit = new();
    }

    /// <summary>Vertical tolerance (m) above/below the aim plane that a swing still connects.</summary>
    public const float VerticalReach = 1.4f;

    private readonly NpcSystem _npcs;
    private readonly List<Swing> _active = new();

    public MeleeSystem(NpcSystem npcs, Interaction.ItemUseSystem uses)
    {
        _npcs = npcs;
        uses.Swung += Start;
    }

    public int ActiveSwings => _active.Count;

    private void Start(PlayerEntity player, ItemUseInfo info)
    {
        var aim = player.Aim;
        _active.RemoveAll(s => s.Player == player);
        _active.Add(new Swing
        {
            Player = player,
            Item = info,
            Duration = Math.Max(2, info.Item.UseAnimation),
            Yaw = MathF.Atan2(aim.X, aim.Z),
            Pitch = MathF.Asin(Math.Clamp(aim.Y, -1f, 1f)),
        });
    }

    public void Tick()
    {
        for (int i = _active.Count - 1; i >= 0; i--)
        {
            var s = _active[i];
            s.Tick++;
            float t = s.Tick / (float)s.Duration;
            foreach (var npc in _npcs.All.ToList())
            {
                if (!npc.Alive || s.Hit.Contains(npc.Id)) continue;
                if (!npc.Volumes().Any(v => Connects(s, (v.Min + v.Max) * 0.5f, (v.Max - v.Min) * 0.5f, t))) continue;
                var item = s.Item.Item;
                if (_npcs.Hit(npc, s.Player, item.Damage, item.DamageType, item.Crit + Combat.DamageModel.BaseCritChance, item.Knockback, s.Player.Center, melee: true))
                    s.Hit.Add(npc.Id);
            }
            if (s.Tick >= s.Duration) _active.RemoveAt(i);
        }
    }

    private static bool Connects(Swing s, Vector3 center, Vector3 half, float t)
    {
        var origin = s.Player.Center + new Vector3(0, 0.3f, 0);
        var to = center - origin;
        float radius = MathF.Max(half.X, half.Y);
        if (s.Item.Has(ItemAction.Thrust))
        {
            float length = s.Item.ThrustParams!.Length * Units.CellSize + 0.4f;
            var dir = new Vector3(MathF.Sin(s.Yaw) * MathF.Cos(s.Pitch), MathF.Sin(s.Pitch), MathF.Cos(s.Yaw) * MathF.Cos(s.Pitch));
            float along = Vector3.Dot(to, dir);
            if (along < 0 || along > length + radius) return false;
            return (to - dir * along).Length() <= radius + 0.45f;
        }
        var swing = s.Item.Swing!;
        float reach = swing.Length * Units.CellSize + 0.6f;
        var flat = new Vector3(to.X, 0, to.Z);
        if (flat.Length() > reach + radius) return false;
        float aimPlaneY = MathF.Tan(s.Pitch) * flat.Length();
        if (MathF.Abs(to.Y - aimPlaneY) > VerticalReach + half.Y) return false;
        if (flat.Length() < 0.6f) return true; // point blank
        // The blade sweeps from +arc/2 to −arc/2 around the aim yaw over the animation.
        float halfArc = swing.Arc * MathF.PI / 360f;
        float current = halfArc - t * 2f * halfArc;
        float angle = WrapAngle(BrainFactory.YawOf(flat) - s.Yaw);
        return angle <= halfArc && angle >= current - 0.25f;
    }

    private static float WrapAngle(float a)
    {
        while (a > MathF.PI) a -= MathF.Tau;
        while (a < -MathF.PI) a += MathF.Tau;
        return a;
    }
}
