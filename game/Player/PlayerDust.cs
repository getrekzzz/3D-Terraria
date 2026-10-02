using Godot;
using Strata.Core.Content.Defs;
using Strata.Game.Audio;
using Strata.Game.Rendering;

namespace Strata.Game.Player;

/// <summary>
/// Movement dust (EffectKit, scaled by the preset): a hard landing kicks up chips in the colour of the ground, jumps
/// and dashes puff, sprinting leaves little puffs; going into or out of water, lava or honey throws a splash ring in
/// the liquid's colour with a splash sound by the speed of the plunge.
/// </summary>
public sealed partial class PlayerDust : Node
{
    private const float LandImpact = 8f, SprintSpeed = 5.5f;
    private static readonly Color Dust = new(0.62f, 0.56f, 0.48f);

    private PlayerController _player = null!;

    /// <summary>Splashes so far (probe <c>player.splashes</c>).</summary>
    public int Splashes { get; private set; }

    public void Initialize(PlayerController player)
    {
        Name = "PlayerDust";
        _player = player;
        player.Landed += impact =>
        {
            if (impact > LandImpact) Burst(EffectRecipe.Chips, 8, 2.2f);
        };
        player.Jumped += () => Burst(EffectRecipe.Dust, 4, 1.2f);
        player.Dashed += () => Burst(EffectRecipe.Dust, 6, 1.8f);
        player.Footstep += speed =>
        {
            if (speed > SprintSpeed) Burst(EffectRecipe.Dust, 2, 0.8f);
        };
        player.LiquidEntered += (liquid, vy) => Splash(liquid, vy);
        player.LiquidExited += (liquid, vy) => Splash(liquid, vy);
    }

    /// <summary>A burst at the feet in the ground block's colour (a neutral dust over unknown ground).</summary>
    private void Burst(EffectRecipe recipe, int count, float speed)
    {
        if (EffectKit.Instance is not { } kit) return;
        var color = _player.GroundBlock() is { } ground ? Color.FromHtml(ground.Color) : Dust;
        kit.Burst(recipe, _player.GlobalPosition + Vector3.Up * 0.08f, color, count, speed, Vector3.Up, 0.6f);
    }

    private void Splash(LiquidDef liquid, float vy)
    {
        Splashes++;
        float strength = Mathf.Clamp(Mathf.Abs(vy) / 10f, 0.25f, 1f);
        var at = _player.GlobalPosition + Vector3.Up * 0.9f;
        EffectKit.Instance?.Burst(EffectRecipe.Splash, at, Color.FromHtml(liquid.Color), 16, 2.5f + 3f * strength, Vector3.Up, 0.7f);
        Sfx.PlayAt("splash", at, Mathf.LinearToDb(strength));
    }
}
