using System.Collections.Generic;
using Godot;
using Strata.Core.Content.Specs;
using Strata.Core.Gameplay.Entities;
using Strata.Game.Rendering;

namespace Strata.Game.Entities;

/// <summary>
/// How creatures move on screen, on top of their clips: humans walk, run and jump by their speed, swing or shoot when
/// they attack, flinch when hit and fall down dead; eyes stare at their target (facing "look"); fliers bank into turns
/// and pitch with their climb; gels squash and stretch; a boss quivers before it charges, stretches along a dash,
/// shrinks away when it teleports and spins as it changes form. The dead shrink away (humans fall and sink).
/// </summary>
public sealed partial class NpcRenderer
{
    private sealed class Corpse
    {
        public required CreatureVisual Visual;
        public required string Model;
        public float Age;
        public Vector3 Scale;
        public bool Human;
    }

    private const float CorpseTime = 1.7f, SinkTime = 0.6f, ShrinkTime = 0.3f;
    private readonly List<Corpse> _corpses = new();

    /// <summary>Turns a (non-worm) creature: yaw, a look at its target or along its flight, with banking for fliers.</summary>
    private void Orient(Npc npc, View view, float dt)
    {
        var v = new Vector3(npc.Velocity.X, npc.Velocity.Y, npc.Velocity.Z);
        Quaternion want;
        switch (npc.Def.Facing)
        {
            case "look" when Target(npc) is { } target:
                want = Look(target - (view.Shown + Vector3.Up * npc.HalfExtents.Y));
                break;
            case "velocity" when v.LengthSquared() > 0.25f:
                want = Look(v);
                break;
            default:
                float yaw = npc.Yaw + Mathf.Pi;
                float turn = Mathf.AngleDifference(view.Yaw, yaw);
                view.Yaw = Mathf.LerpAngle(view.Yaw, yaw, 1f - Mathf.Exp(-dt * 10f));
                want = new Quaternion(Vector3.Up, view.Yaw);
                if (!npc.Def.Gravity && npc.Segments.Count == 0)
                {
                    // Fliers lean into turns and tip their nose with their climb.
                    float h = new Vector2(v.X, v.Z).Length();
                    float pitch = Mathf.Clamp(Mathf.Atan2(v.Y, Mathf.Max(h, 1f)) * 0.6f, -0.5f, 0.5f);
                    float bank = Mathf.Clamp(-turn * 1.5f, -0.45f, 0.45f);
                    view.Bank = Mathf.Lerp(view.Bank, bank, 1f - Mathf.Exp(-dt * 4f));
                    want *= new Quaternion(Vector3.Right, pitch) * new Quaternion(Vector3.Back, view.Bank);
                }
                view.Turn = want;
                return;
        }
        // Renormalised each frame: repeated slerps drift off unit length, which Godot rejects.
        view.Turn = view.Turn.Normalized().Slerp(want.Normalized(), 1f - Mathf.Exp(-dt * 8f)).Normalized();
        view.Yaw = view.Turn.GetEuler().Y;
    }

    private static Quaternion Look(Vector3 dir)
    {
        if (dir.LengthSquared() < 1e-6f) return Quaternion.Identity;
        dir = dir.Normalized();
        return Basis.LookingAt(dir, Mathf.Abs(dir.Y) > 0.98f ? Vector3.Forward : Vector3.Up).GetRotationQuaternion();
    }

    private Vector3? Target(Npc npc)
    {
        if (npc.TargetPlayer < 0 || npc.TargetPlayer >= _game.Players.Count) return null;
        var p = _game.Players[(int)npc.TargetPlayer];
        return p.Alive ? new Vector3(p.Eye.X, p.Eye.Y, p.Eye.Z) : null;
    }

    private void Animate(Npc npc, View view, float dt)
    {
        var visual = view.Visual;
        float s = npc.Def.Scale;
        var scale = Vector3.One * s;
        if (npc.Def.Squash || npc.Def.Brain.Type == BrainSpecs.SlimeHop)
        {
            // Squash on landing, stretch while rising.
            if (npc.OnGround && view.LastVy < -2f) view.Squash = 0.65f;
            float want = npc.OnGround ? 1f : Mathf.Clamp(1f + npc.Velocity.Y * 0.04f, 0.85f, 1.25f);
            view.Squash = Mathf.Lerp(view.Squash, want, 1f - Mathf.Exp(-dt * 12f));
            scale = new Vector3(s / Mathf.Sqrt(view.Squash), s * view.Squash, s / Mathf.Sqrt(view.Squash));
            view.LastVy = npc.Velocity.Y;
        }
        float speed = new Vector3(npc.Velocity.X, npc.Velocity.Y, npc.Velocity.Z).Length();
        var turn = view.Turn;
        if (npc.Pose == "telegraph")
        {
            // Winding up: a tense quiver.
            float t = npc.Age * 0.9f;
            turn *= new Quaternion(Vector3.Right, Mathf.Sin(t * 2.3f) * 0.05f) * new Quaternion(Vector3.Up, Mathf.Sin(t * 3.1f) * 0.06f);
            scale *= 1f + 0.03f * Mathf.Sin(t * 5f);
        }
        float stretch = npc.Pose == "attack" && npc.Segments.Count == 0 && !npc.Def.Gravity ? Mathf.Clamp((speed - 6f) / 40f, 0f, 0.18f) : 0f;
        view.Stretch = Mathf.Lerp(view.Stretch, stretch, 1f - Mathf.Exp(-dt * 10f));
        scale *= new Vector3(1f - view.Stretch * 0.5f, 1f - view.Stretch * 0.5f, 1f + view.Stretch);
        view.Blink = Mathf.MoveToward(view.Blink, npc.Pose == "teleport" ? 0.25f : 1f, dt * 4f);
        scale *= view.Blink;
        if (view.Spin > 0f)
        {
            // Changing form: a fast spin that slows to a stop.
            view.Spin = Mathf.Max(0f, view.Spin - dt);
            turn = new Quaternion(Vector3.Up, Mathf.Tau * 3f * Mathf.Pow(view.Spin, 2f)) * turn;
        }
        // A spinning skull whirls about its own axis (its simulated facing holds still so its hands keep their sides);
        // after the spin it winds on to the next full turn.
        view.Whirl = npc.Pose == "spin" ? (view.Whirl + dt * 18f) % Mathf.Tau
            : view.Whirl > 0f ? Mathf.MoveToward(view.Whirl, Mathf.Tau, dt * 12f) % Mathf.Tau : 0f;
        if (view.Whirl > 0f) turn = new Quaternion(Vector3.Up, view.Whirl) * turn;
        if (npc.Mirrored) scale.X = -scale.X; // a left hand (after the facing, so it mirrors in the model's own frame)
        if (npc.Segments.Count == 0 || npc.Def.Boss!.SegmentModels.Length > 0) visual.Basis = new Basis(turn) * Basis.FromScale(scale);
        else visual.Scale = scale;

        if (visual.Humanoid is { } h)
        {
            AnimateHuman(npc, view, h);
            return;
        }
        string clip = npc.Pose switch
        {
            "move" => visual.HasAnimation("walk") ? "walk" : "fly",
            "attack" => visual.HasAnimation("attack") ? "attack" : visual.HasAnimation("fly") ? "fly" : "walk",
            "jump" => "jump",
            _ => visual.HasAnimation("idle") ? "idle" : "fly",
        };
        float rate = clip == "walk" ? Mathf.Max(0.5f, new Vector2(npc.Velocity.X, npc.Velocity.Z).Length() / 2.5f) : (clip == "fly" && npc.Pose == "attack" ? 2f : 1f);
        visual.Play(clip, speed: rate);
        if (!visual.HasAnimation("fly") && !visual.HasAnimation("walk") && !npc.Def.Gravity)
            visual.Position += new Vector3(0, Mathf.Sin(npc.Age * 0.08f) * 0.08f, 0); // bobbing eye
    }

    /// <summary>Human creatures: gait by speed, a swing (or shot, or spell) when attacking, a flinch when hit.</summary>
    private void AnimateHuman(Npc npc, View view, HumanoidAnimator h)
    {
        float ground = new Vector2(npc.Velocity.X, npc.Velocity.Z).Length();
        float rate = 1f;
        string state = npc.Def.Gravity && !npc.OnGround ? (npc.Velocity.Y > 0.5f ? "jump" : "fall")
            : npc.Pose == "jump" ? "jump" : ground > 0.3f ? h.Gait(ground, out rate) : "idle";
        h.Locomotion(state, rate);
        bool fired = npc.AttackTimer > view.LastAttackTimer + 1;
        view.LastAttackTimer = npc.AttackTimer;
        if (fired && npc.Def.Ranged != null)
        {
            bool magic = npc.Def.Ranged.Projectile.Contains("bolt") || npc.Def.Ranged.Projectile.Contains("magic") || npc.Def.HasTag("caster");
            h.Play(magic ? "cast" : npc.Def.Ranged.Projectile.Contains("arrow") ? "bow" : "shoot", 0.5f);
        }
        else if (npc.Def.Ranged == null && Target(npc) is { } target && npc.Def.ContactDamage)
        {
            // Melee folk swing when they reach you.
            var at = view.Shown + Vector3.Up * npc.HalfExtents.Y;
            if (at.DistanceTo(target) < 2.4f && npc.Age - view.LastSwing > 55)
            {
                view.LastSwing = npc.Age;
                h.Play("swing", 0.5f);
            }
        }
        if (npc.HurtFlash > view.LastHurtFlash) h.Hurt();
        view.LastHurtFlash = npc.HurtFlash;
    }

    /// <summary>A creature that died: humans play their death and sink into the ground, the rest shrink away.</summary>
    private bool KeepCorpse(Npc npc, View view)
    {
        if (npc.Alive || npc.Segments.Count > 0 || npc.Barrier != null) return false;
        var visual = view.Visual;
        visual.Humanoid?.Locomotion("death");
        _corpses.Add(new Corpse { Visual = visual, Model = view.Model, Scale = visual.Scale, Human = visual.Humanoid != null });
        return true;
    }

    private void UpdateCorpses(float dt)
    {
        for (int i = _corpses.Count - 1; i >= 0; i--)
        {
            var c = _corpses[i];
            c.Age += dt;
            float end = c.Human ? CorpseTime + SinkTime : ShrinkTime;
            if (c.Age >= end)
            {
                c.Visual.Scale = c.Scale;
                Release(c.Visual, c.Model);
                _corpses.RemoveAt(i);
                continue;
            }
            if (c.Human)
            {
                if (c.Age > CorpseTime) c.Visual.Position += Vector3.Down * (0.9f / SinkTime) * dt;
            }
            else c.Visual.Scale = c.Scale * Mathf.Max(0.01f, 1f - c.Age / ShrinkTime);
        }
    }
}
