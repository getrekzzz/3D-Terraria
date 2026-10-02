using System.Collections.Generic;
using Godot;
using Strata.Core.Gameplay.Entities;
using Strata.Core.Session;
using Strata.Game.Rendering;

namespace Strata.Game.Entities;

/// <summary>Floating damage numbers, death puffs, and liquid reactions (steam where liquids harden, items burning up).</summary>
public sealed partial class CombatEffects : Node3D
{
    private readonly Queue<Label3D> _pool = new();
    private EffectKit _kit = null!;
    private FlashLights _flashes = null!;

    public void Initialize(GameWorld game, EffectKit kit, FlashLights flashes, GoreSystem gore)
    {
        Name = "CombatEffects";
        _kit = kit;
        _flashes = flashes;
        _gore = gore;
        InitializeDust(game);
        game.Npcs.Damaged += e => Number(e);
        game.Town.Hurt += (t, dmg) => Number(new DamageEvent(t.Center + new System.Numerics.Vector3(0, 1.1f, 0), dmg, false, true));
        game.Liquids.Mixed += (cell, _) => Puff((new Vector3(cell.X, cell.Y, cell.Z) + new Vector3(0.5f, 1f, 0.5f)) * Strata.Core.Common.Units.CellSize, new Color(0.85f, 0.85f, 0.88f), 0.6f, rise: true);
        game.LiquidEffects.ItemDestroyed += (item, liquid) => Puff(new Vector3(item.Position.X, item.Position.Y, item.Position.Z), Color.FromHtml(liquid.Color), 0.2f, rise: true);
        game.Projectiles.Exploded += (at, radius) => Explosion(new Vector3(at.X, at.Y, at.Z), radius);
    }

    /// <summary>A blast: a bright flash of light, a fireball and a cloud of smoke scaled to the radius.</summary>
    private void Explosion(Vector3 at, float radius)
    {
        _flashes.Flash(at, new Color(1f, 0.75f, 0.4f), 6f, radius * 4f + 4f, 0.45f);
        int n = Mathf.RoundToInt(10 + radius * 6f);
        _kit.Burst(EffectRecipe.Sparks, at, new Color(1f, 0.62f, 0.2f), n, 6f + radius * 2f);
        _kit.Burst(EffectRecipe.Puff, at, new Color(0.25f, 0.23f, 0.22f), n, 2f + radius);
        _kit.Burst(EffectRecipe.Puff, at, new Color(0.55f, 0.52f, 0.5f), n / 2, 1.5f + radius);
        Player.CameraShake.Kick(Mathf.Clamp(radius * 0.35f, 0.2f, 1f));
    }

    private void Number(DamageEvent e)
    {
        if (!Settings.UserSettings.Current.DamageNumbers) return;
        var label = _pool.Count > 0 ? _pool.Dequeue() : NewLabel();
        label.Visible = true;
        label.Text = e.Amount.ToString();
        label.FontSize = e.Crit ? 72 : e.Kind == DamageKind.Periodic ? 40 : 56;
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

    /// <summary>A soft cloud (death puffs, steam, burning items) through the effect kit; rising clouds drift up.</summary>
    private void Puff(Vector3 at, Color color, float size, bool rise = false) =>
        _kit.Burst(rise ? EffectRecipe.Puff : EffectRecipe.Dust, at, color, Mathf.RoundToInt(10 + size * 8f), 1.5f + size);
}
