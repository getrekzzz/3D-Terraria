using Godot;
using Strata.Core.Session;

namespace Strata.Game.Entities;

/// <summary>Floating combat numbers (CombatEffects.Numbers.cs), death puffs, explosions, and liquid reactions (steam where liquids harden, items burning up).</summary>
public sealed partial class CombatEffects : Node3D
{
    public void Initialize(GameWorld game, Rendering.GraphicsSettings graphics)
    {
        Name = "CombatEffects";
        InitializeNumbers(game, graphics);
        game.Npcs.Killed += npc => Puff(new Vector3(npc.Center.X, npc.Center.Y, npc.Center.Z), Color.FromHtml(npc.Def.Tint), npc.HalfExtents.Y);
        game.Liquids.Mixed += (cell, _) => Puff((new Vector3(cell.X, cell.Y, cell.Z) + new Vector3(0.5f, 1f, 0.5f)) * Strata.Core.Common.Units.CellSize, new Color(0.85f, 0.85f, 0.88f), 0.6f, rise: true);
        game.LiquidEffects.ItemDestroyed += (item, liquid) => Puff(new Vector3(item.Position.X, item.Position.Y, item.Position.Z), Color.FromHtml(liquid.Color), 0.2f, rise: true);
        game.Projectiles.Exploded += (at, radius) => Explosion(new Vector3(at.X, at.Y, at.Z), radius);
    }

    /// <summary>A blast: a bright flash of light, a fireball and a cloud of smoke scaled to the radius.</summary>
    private void Explosion(Vector3 at, float radius)
    {
        var flash = new OmniLight3D { LightColor = new Color(1f, 0.75f, 0.4f), OmniRange = radius * 4f + 4f, LightEnergy = 6f, ShadowEnabled = false, Position = at };
        AddChild(flash);
        var fade = flash.CreateTween();
        fade.TweenProperty(flash, "light_energy", 0.0, 0.45).SetEase(Tween.EaseType.Out);
        fade.TweenCallback(Callable.From(flash.QueueFree));
        Puff(at, new Color(1f, 0.62f, 0.2f), radius * 2.5f);
        Puff(at, new Color(0.25f, 0.23f, 0.22f), radius * 3.5f, rise: true);
        Puff(at, new Color(0.55f, 0.52f, 0.5f), radius * 3f, rise: true);
        Player.CameraShake.Kick(Mathf.Clamp(radius * 0.35f, 0.2f, 1f));
    }

    private void Puff(Vector3 at, Color color, float size, bool rise = false)
    {
        var particles = new CpuParticles3D
        {
            Emitting = false,
            OneShot = true,
            Amount = 18,
            Lifetime = 0.7,
            Explosiveness = 0.95f,
            Direction = Vector3.Up,
            Spread = 180f,
            InitialVelocityMin = 1.5f,
            InitialVelocityMax = 3.5f,
            Gravity = new Vector3(0, rise ? 1.5f : -6f, 0),
            ScaleAmountMin = 0.6f,
            ScaleAmountMax = 1.2f,
            Color = color,
            Mesh = new SphereMesh { Radius = 0.06f + size * 0.05f, Height = 0.12f + size * 0.1f, RadialSegments = 6, Rings = 3 },
            Position = at,
        };
        particles.MaterialOverride = new StandardMaterial3D { VertexColorUseAsAlbedo = true, ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded };
        AddChild(particles);
        particles.Emitting = true;
        particles.Finished += particles.QueueFree;
    }
}
