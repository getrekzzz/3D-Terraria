using Godot;
using Strata.Core.Session;
using Strata.Game.Rendering;

namespace Strata.Game.Entities;

/// <summary>Floating combat numbers (CombatEffects.Numbers.cs), death puffs, explosions, and liquid reactions (steam where liquids harden, items burning up).</summary>
public sealed partial class CombatEffects : Node3D
{
    private EffectKit _kit = null!;
    private FlashLights _flashes = null!;

    public void Initialize(GameWorld game, Rendering.GraphicsSettings graphics, EffectKit kit, FlashLights flashes, GoreSystem gore)
    {
        Name = "CombatEffects";
        _kit = kit;
        _flashes = flashes;
        _gore = gore;
        InitializeDust(game); // hit dust, death bursts and gore (CombatEffects.Dust.cs)
        InitializeNumbers(game, graphics);
        InitializeMuzzle(game); // muzzle flashes, tip sparks and summon puffs (CombatEffects.Muzzle.cs)
        InitializeImpacts(game); // chips where shots strike the terrain (CombatEffects.Impacts.cs)
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

    /// <summary>A soft cloud (steam, burning items) through the effect kit; rising clouds drift up.</summary>
    private void Puff(Vector3 at, Color color, float size, bool rise = false) =>
        _kit.Burst(rise ? EffectRecipe.Puff : EffectRecipe.Dust, at, color, Mathf.RoundToInt(10 + size * 8f), 1.5f + size);
}
