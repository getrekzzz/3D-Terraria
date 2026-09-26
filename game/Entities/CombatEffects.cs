using System.Collections.Generic;
using Godot;
using Strata.Core.Gameplay.Entities;
using Strata.Core.Session;

namespace Strata.Game.Entities;

/// <summary>Floating damage numbers, death puffs, and liquid reactions (steam where liquids harden, items burning up).</summary>
public sealed partial class CombatEffects : Node3D
{
    private readonly Queue<Label3D> _pool = new();

    public void Initialize(GameWorld game)
    {
        Name = "CombatEffects";
        game.Npcs.Damaged += e => Number(e);
        game.Npcs.Killed += npc => Puff(new Vector3(npc.Center.X, npc.Center.Y, npc.Center.Z), Color.FromHtml(npc.Def.Tint), npc.HalfExtents.Y);
        game.Town.Hurt += (t, dmg) => Number(new DamageEvent(t.Center + new System.Numerics.Vector3(0, 1.1f, 0), dmg, false, true));
        game.Liquids.Mixed += (cell, _) => Puff((new Vector3(cell.X, cell.Y, cell.Z) + new Vector3(0.5f, 1f, 0.5f)) * Strata.Core.Common.Units.CellSize, new Color(0.85f, 0.85f, 0.88f), 0.6f, rise: true);
        game.LiquidEffects.ItemDestroyed += (item, liquid) => Puff(new Vector3(item.Position.X, item.Position.Y, item.Position.Z), Color.FromHtml(liquid.Color), 0.2f, rise: true);
    }

    private void Number(DamageEvent e)
    {
        if (!Settings.UserSettings.Current.DamageNumbers) return;
        var label = _pool.Count > 0 ? _pool.Dequeue() : NewLabel();
        label.Visible = true;
        label.Text = e.Amount.ToString();
        label.FontSize = e.Crit ? 72 : 56;
        label.Modulate = e.ToPlayer ? new Color(1f, 0.3f, 0.3f) : e.Crit ? new Color(1f, 0.6f, 0.15f) : new Color(1f, 0.9f, 0.45f);
        var start = new Vector3(e.Position.X, e.Position.Y, e.Position.Z) + new Vector3((float)GD.RandRange(-0.3, 0.3), 0, (float)GD.RandRange(-0.3, 0.3));
        label.Position = start;
        var tween = label.CreateTween();
        tween.SetParallel();
        tween.TweenProperty(label, "position", start + new Vector3(0, 1.1f, 0), 0.9).SetEase(Tween.EaseType.Out).SetTrans(Tween.TransitionType.Cubic);
        tween.TweenProperty(label, "modulate:a", 0.0, 0.9).SetDelay(0.3);
        tween.Chain().TweenCallback(Callable.From(() =>
        {
            label.Visible = false;
            _pool.Enqueue(label);
        }));
    }

    private Label3D NewLabel()
    {
        var label = new Label3D
        {
            Billboard = BaseMaterial3D.BillboardModeEnum.Enabled,
            PixelSize = 0.005f,
            OutlineSize = 14,
            OutlineModulate = new Color(0, 0, 0, 0.85f),
            NoDepthTest = true,
            FixedSize = false,
        };
        AddChild(label);
        return label;
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
