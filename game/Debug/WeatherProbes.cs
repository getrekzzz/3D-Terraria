using Strata.Core.Commands;
using Strata.Game.Bootstrap;

namespace Strata.Game.Debug;

/// <summary>
/// Weather presentation probes: <c>weather.particles</c> (particles alive at most across rain, snow, sand and splashes,
/// always within the preset's budget), <c>weather.budget</c> (that budget's total), <c>weather.wetness</c> (terrain
/// wetness 0–1) and <c>weather.strikes</c> (lightning strikes so far).
/// </summary>
[ProbeSource]
public static class WeatherProbes
{
    public static void Register(CommandContext ctx, LaunchedWorld world)
    {
        var runtime = world.Runtime;
        ctx.AddProbe("weather.particles", () => runtime.Weather?.ActiveParticles ?? 0);
        ctx.AddProbe("weather.budget", () => runtime.Weather?.BudgetTotal ?? 0);
        ctx.AddProbe("weather.wetness", () => runtime.Weather?.Wetness ?? 0);
        ctx.AddProbe("weather.strikes", () => runtime.Weather?.Strikes ?? 0);
    }
}

/// <summary>
/// Ambience probes: <c>ambience.loops</c> (sound beds audible now), <c>ambience.particles</c> (ambient particles alive
/// at most), <c>audio.reverb</c> (reverb wet level on world sounds) and <c>audio.muffle</c> (outdoors low-pass, Hz).
/// </summary>
[ProbeSource]
public static class AmbienceProbes
{
    public static void Register(CommandContext ctx, LaunchedWorld world)
    {
        var runtime = world.Runtime;
        ctx.AddProbe("ambience.loops", () => runtime.Ambience?.AudibleLoops ?? 0);
        ctx.AddProbe("ambience.particles", () => runtime.Ambience?.Particles.Alive ?? 0);
        ctx.AddProbe("audio.reverb", () => Audio.AudioEnvironment.ReverbWet);
        ctx.AddProbe("audio.muffle", () => Audio.AudioEnvironment.OutdoorsCutoff);
    }
}
