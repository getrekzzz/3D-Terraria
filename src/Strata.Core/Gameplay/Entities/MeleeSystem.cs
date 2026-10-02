using System.Numerics;
using Strata.Core.AI;
using Strata.Core.Common;
using Strata.Core.Gameplay.Items;
using Strata.Core.Session;

namespace Strata.Core.Gameplay.Entities;

/// <summary>
/// Resolves melee swings and thrusts over their use animation: after the clip's wind-up the blade sweeps the overhead
/// arc of <see cref="SwingCurve.Blade"/> in its <see cref="SwingPlane"/> through the strike window and follow-through
/// (or stabs along the aim while a thrust is extended, MeleeSystem.Curve), and each creature is hit at most once per
/// swing. Windows come from <see cref="Interaction.ItemUseSystem.Timing"/>.
/// </summary>
public sealed partial class MeleeSystem
{
    private sealed class Swing
    {
        public required PlayerEntity Player { get; init; }
        public required ItemUseInfo Item { get; init; }
        public required int Duration { get; init; }
        public required float Yaw { get; init; }
        public required float Pitch { get; init; }

        /// <summary>The swung stack (its prefix changes damage, crit, knockback and reach).</summary>
        public required Items.ItemStack Stack { get; init; }
        public int Tick;

        /// <summary>The blade angle (degrees, curve value) the last hit test swept to.</summary>
        public float LastDeg = float.NaN;
        public readonly HashSet<long> Hit = new();
    }

    /// <summary>Vertical tolerance (m) above/below the aim plane that a swing still connects.</summary>
    public const float VerticalReach = 1.4f;

    private readonly NpcSystem _npcs;
    private readonly Interaction.ItemUseSystem _uses;
    private readonly List<Swing> _active = new();

    public MeleeSystem(NpcSystem npcs, Interaction.ItemUseSystem uses)
    {
        _npcs = npcs;
        _uses = uses;
        uses.Swung += Start;
    }

    public int ActiveSwings => _active.Count;

    private void Start(PlayerEntity player, ItemUseInfo info)
    {
        // The arc is centred on where the player looks; the aim (eye to crosshair point) bends sideways towards
        // close targets off the shoulder camera, so it only sets the pitch.
        var aim = player.Aim;
        var facing = player.Facing;
        _active.RemoveAll(s => s.Player == player);
        _active.Add(new Swing
        {
            Player = player,
            Item = info,
            Duration = Math.Max(2, player.UseAnimationMax), // the animation after speed bonuses
            Yaw = MathF.Atan2(facing.X, facing.Z),
            Pitch = MathF.Asin(Math.Clamp(aim.Y, -1f, 1f)),
            Stack = player.State.Inventory.Held.Item == info.Item ? player.State.Inventory.Held : new Items.ItemStack(info.Item, 1),
        });
    }

    public void Tick()
    {
        var timing = _uses.Timing;
        for (int i = _active.Count - 1; i >= 0; i--)
        {
            var s = _active[i];
            s.Tick++;
            float t = s.Tick / (float)s.Duration;
            Strike(s, timing, (s.Tick - 1) / (float)s.Duration, t);
            if (s.Tick >= s.Duration) _active.RemoveAt(i);
        }
    }

    private void HitCreatures(Swing s, Func<Vector3, Vector3, bool> connects)
    {
        foreach (var npc in _npcs.All.ToList())
        {
            if (!npc.Alive || s.Hit.Contains(npc.Id)) continue;
            int volume = npc.FirstVolume(connects);
            if (volume < 0) continue;
            var item = s.Item.Item;
            if (_npcs.Hit(npc, s.Player, Items.Prefixes.Damage(s.Stack), item.DamageType, Items.Prefixes.Crit(s.Stack) + Combat.DamageModel.BaseCritChance,
                    Items.Prefixes.Knockback(s.Stack), s.Player.Center, melee: true, segment: volume - 1))
            {
                s.Hit.Add(npc.Id);
                LastHit = new MeleeHit(npc.Id, _hitDeg, Vector3.Distance(_hitPivot, npc.Center));
            }
        }
    }
}
