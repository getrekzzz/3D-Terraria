using Strata.Core.Commands;
using Strata.Game.Audio;
using Strata.Game.Bootstrap;
using Strata.Game.Player;

namespace Strata.Game.Debug;

/// <summary>
/// Probes on boss presentation: the camera's shake trauma, whether the soundtrack is held silent after a win, the track
/// playing (<c>music.track.&lt;id&gt;</c>: 1 when it is that track) and the stings played, and the title card (X-02):
/// <c>boss.intro_active</c>, <c>boss.intro_defeated</c> (the "Defeated" card is up), <c>boss.intro_cards</c> and
/// <c>boss.intro_skips</c> (counts), <c>boss.intro_bars</c> (cinema bars, 0–1), <c>camera.framing</c> (metres pulled
/// back) and <c>engine.time_scale</c> (the engine's time scale, below 1 during a defeat's slow motion; Core's <c>time.scale</c> is the sleeping clock's speed).
/// </summary>
[ProbeSource]
public static class BossEffectProbes
{
    public static void Register(CommandContext ctx, LaunchedWorld world)
    {
        ctx.AddProbe("camera.trauma", () => CameraShake.Trauma);
        ctx.AddProbe("music.silent", () => MusicDirector.Instance?.Silent == true ? 1 : 0);
        ctx.AddProbe("music.stings", () => MusicDirector.Instance?.Stings ?? 0);
        ctx.AddPrefixProbe("music.track.", id => MusicDirector.Instance?.Current == id ? 1 : 0);
        ctx.AddProbe("engine.time_scale", () => Godot.Engine.TimeScale);
        UI.BossIntro? Intro() => world.Runtime.GetNodeOrNull<UI.BossIntro>("BossIntro");
        ctx.AddProbe("boss.intro_active", () => Intro()?.Model.Active == true ? 1 : 0);
        ctx.AddProbe("boss.intro_defeated", () => Intro()?.Model.Kind == Core.Presentation.BossCardKind.Defeated ? 1 : 0);
        ctx.AddProbe("boss.intro_cards", () => Intro()?.Model.Shown ?? 0);
        ctx.AddProbe("boss.intro_skips", () => Intro()?.Model.Skips ?? 0);
        ctx.AddProbe("boss.intro_bars", () => Intro()?.Model.Bars ?? 0);
        ctx.AddProbe("camera.framing", () => world.Player?.Rig.Framing ?? 0);
    }
}
