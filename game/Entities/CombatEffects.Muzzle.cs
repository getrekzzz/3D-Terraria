using Godot;
using Strata.Core.Gameplay.Entities;
using Strata.Core.Session;
using Strata.Game.Player;
using Strata.Game.Rendering;

namespace Strata.Game.Entities;

/// <summary>
/// Shot presentation (P3-10) for the local player: guns (use style <c>muzzleFlash</c>) flash at the muzzle with a wisp
/// of smoke and a brief light, heavy guns (use time ≥ 30) punch the camera, staves and tomes (<c>tipSparks</c>) spark
/// at the tip in the shot's colour, and summoned minions pop in with a puff. One flash per tick however many pellets
/// a shotgun fires. Effects sit at the held model's muzzle or tip in the active view (HeldSockets), else where the
/// shot started.
/// </summary>
public sealed partial class CombatEffects
{
    private MuzzleFlashes _muzzle = null!;
    private long _flashTick = -1;

    /// <summary>Use time from which a gun's shot punches the camera.</summary>
    private const int HeavyUseTime = 30;

    public MuzzleFlashes Muzzle => _muzzle;

    private void InitializeMuzzle(GameWorld game)
    {
        _muzzle = new MuzzleFlashes();
        AddChild(_muzzle);
        game.Projectiles.Spawned += OnShotSpawned;
    }

    private void OnShotSpawned(Projectile p)
    {
        if (p.Owner == null || p.Owner != _game.LocalPlayer || p.PetSlot != null || p.Def.Bobber) return;
        var color = Color.FromHtml(p.Def.Color);
        if (p.Def.Minion != null)
        {
            _kit.Burst(EffectRecipe.Puff, ToGodot(p.Position), color.Lerp(Colors.White, 0.5f), 14, 1.6f);
            return;
        }
        if (p.FromMinion || p.Source == null || _flashTick == _game.Session.Clock.Tick) return;
        var style = _game.Styles.Style(_game.Uses[p.Source].Style);
        if (!style.MuzzleFlash && !style.TipSparks) return;
        var origin = ToGodot(p.SpawnOrigin);
        if (origin.DistanceTo(ToGodot(p.Owner.Eye)) > 3f) return; // shot from the sky or the crosshair: nothing at the hand
        _flashTick = _game.Session.Clock.Tick;
        var at = HeldSockets.TryBusinessEnd(out var socket) ? socket : origin;
        var dir = ToGodot(p.Velocity);
        dir = dir.LengthSquared() > 1e-4f ? dir.Normalized() : Vector3.Forward;
        if (style.MuzzleFlash)
        {
            bool heavy = p.Source.UseTime >= HeavyUseTime;
            _muzzle.Flash(at + dir * 0.05f, color, heavy ? 0.45f : 0.3f);
            _flashes.Flash(at, new Color(1f, 0.85f, 0.55f).Lerp(color, 0.3f), 2f, 4f, 0.06f);
            // A wisp of smoke, kept out of a first-person view (a puff that close to the eye fills the screen grey).
            var eye = GetViewport()?.GetCamera3D()?.GlobalPosition;
            if (eye == null || eye.Value.DistanceTo(at) > 1.5f) _kit.Burst(EffectRecipe.Puff, at + dir * 0.15f, new Color(0.7f, 0.68f, 0.66f), heavy ? 4 : 2, 0.5f, dir);
            if (heavy) CameraShake.Punch(0.18f, 0.12f, 0.6f);
        }
        else
        {
            _kit.Burst(EffectRecipe.Sparks, at, color, 8, 2.5f, dir, 0.6f);
        }
    }
}
