using System.Numerics;
using Strata.Core.Determinism;
using Strata.Core.Session;
using Strata.Core.Simulation;

namespace Strata.Core.Gameplay.Weather;

/// <summary>
/// Schedules the world's weather like the reference game: once per in-game hour a roll (seeded by the world seed and
/// the hour, so a world's weather never depends on frame timing) may start a rain spell or thunderstorm, turn the day
/// windy at dawn or blow up a sandstorm on a windy day, and picks the hour's wind. Precipitation and wind ease in and
/// out every tick. The matching world events (rain, thunderstorm, windy_day, sandstorm) start and stop with it, so
/// announcements, spawn rules ({ "type": "event" }) and music follow the weather.
/// </summary>
public sealed class WeatherSystem
{
    /// <summary>After a long time skip only the latest hours are rolled (older ones are long over).</summary>
    private const int MaxCatchUpHours = 48;

    private readonly GameWorld _world;
    private readonly ulong _seed;
    private long _lastTick;
    private bool? _rain, _storm, _windy, _sandstorm;

    public WeatherSystem(GameWorld world, WeatherConfig? config = null)
    {
        _world = world;
        _seed = Hash.Combine((ulong)world.Session.Settings.Seed, 0x5745415448UL);
        if (world.Session.Meta.Weather is { } saved) State.Load(saved);
        Config = config ?? new WeatherConfig();
        _lastTick = world.Session.Clock.Tick;
    }

    public WeatherState State => _world.Session.Weather;

    public WeatherConfig Config
    {
        get => State.Config;
        set => State.Config = value;
    }

    public void Tick()
    {
        var clock = _world.Session.Clock;
        long now = clock.Tick;
        int dt = (int)Math.Clamp(now - _lastTick, 0, int.MaxValue);
        _lastTick = now;
        Count(dt);
        long hour = now / GameClock.TicksPerHour;
        if (State.LastHour < 0) State.LastHour = hour - 1;
        if (hour > State.LastHour)
        {
            for (long h = Math.Max(State.LastHour + 1, hour - MaxCatchUpHours + 1); h <= hour; h++) Roll(h);
            State.LastHour = hour;
        }
        Ease(dt);
        Sync();
    }

    /// <summary>Starts precipitation now (commands). <paramref name="hours"/> ≤ 0 picks a natural length.</summary>
    public void StartPrecipitation(Precipitation kind, float hours = 0f, float intensity = -1f)
    {
        var s = State;
        if (kind == Precipitation.None)
        {
            s.Precipitation = Precipitation.None;
            s.PrecipitationTicksLeft = 0;
            Sync();
            return;
        }
        var rng = new Pcg32(Hash.Combine(_seed, _world.Session.Clock.Tick));
        bool storm = kind == Precipitation.Storm;
        var range = storm ? Config.StormIntensity : Config.RainIntensity;
        s.Precipitation = kind;
        s.Intensity = intensity >= 0f ? Math.Clamp(intensity, 0f, 1f) : rng.Range(range[0], range[1]);
        s.PrecipitationTicksLeft = Ticks(hours > 0f ? hours : rng.Range(Config.RainHours[0], Config.RainHours[1]));
        if (storm) StrengthenWind(ref rng);
        Sync();
    }

    public void SetWindy(bool windy, float hours = 0f)
    {
        var s = State;
        s.Windy = windy;
        s.WindyTicksLeft = windy ? Ticks(hours > 0f ? hours : Config.WindyHours[1]) : 0;
        if (!windy) s.Sandstorm = false;
        var rng = new Pcg32(Hash.Combine(_seed, _world.Session.Clock.Tick + 1));
        PickWind(ref rng);
        s.Wind = s.WindTarget; // a command shows its effect at once
        Sync();
    }

    public void SetSandstorm(bool on, float hours = 0f)
    {
        var s = State;
        if (on && !s.Windy) SetWindy(true, Math.Max(hours, Config.WindyHours[0]));
        s.Sandstorm = on;
        s.SandstormTicksLeft = on ? Ticks(hours > 0f ? hours : Config.SandstormHours[1]) : 0;
        Sync();
    }

    /// <summary>Writes the weather into the world metadata (called before saving).</summary>
    public void Persist() => _world.Session.Meta.Weather = State.ToModel();

    private void Count(int dt)
    {
        var s = State;
        if (s.Precipitation != Precipitation.None && (s.PrecipitationTicksLeft -= dt) <= 0)
        {
            s.Precipitation = Precipitation.None;
            s.PrecipitationTicksLeft = 0;
        }
        if (s.Windy && (s.WindyTicksLeft -= dt) <= 0)
        {
            s.Windy = false;
            s.WindyTicksLeft = 0;
        }
        if (s.Sandstorm && ((s.SandstormTicksLeft -= dt) <= 0 || !s.Windy))
        {
            s.Sandstorm = false;
            s.SandstormTicksLeft = 0;
        }
    }

    /// <summary>One hour's rolls; the order of draws is fixed so a seed always gives the same weather.</summary>
    private void Roll(long hour)
    {
        var s = State;
        var c = Config;
        var rng = new Pcg32(Hash.Combine(_seed, hour));
        bool rain = rng.Chance(c.RainChancePerHour);
        bool storm = rng.Chance(c.StormShare);
        float rainHours = rng.Range(c.RainHours[0], c.RainHours[1]);
        float intensity = storm ? rng.Range(c.StormIntensity[0], c.StormIntensity[1]) : rng.Range(c.RainIntensity[0], c.RainIntensity[1]);
        bool windy = (int)(hour % 24) == c.WindyRollHour && rng.Chance(c.WindyChancePerDay);
        float windyHours = rng.Range(c.WindyHours[0], c.WindyHours[1]);
        bool sand = rng.Chance(c.SandstormChancePerHour);
        float sandHours = rng.Range(c.SandstormHours[0], c.SandstormHours[1]);

        if (s.Precipitation == Precipitation.None && rain)
        {
            s.Precipitation = storm ? Precipitation.Storm : Precipitation.Rain;
            s.Intensity = intensity;
            s.PrecipitationTicksLeft = Ticks(rainHours);
        }
        if (!s.Windy && windy)
        {
            s.Windy = true;
            s.WindyTicksLeft = Ticks(windyHours);
        }
        if (s.Windy && !s.Sandstorm && sand)
        {
            s.Sandstorm = true;
            s.SandstormTicksLeft = Ticks(sandHours);
        }
        PickWind(ref rng);
    }

    /// <summary>The hour's wind: calm or windy speed, the heading drifting from the last one.</summary>
    private void PickWind(ref Pcg32 rng)
    {
        var s = State;
        var c = Config;
        var range = s.Windy ? c.WindyWind : c.CalmWind;
        float speed = rng.Range(range[0], range[1]);
        if (s.Precipitation == Precipitation.Storm) speed = Math.Max(speed, c.StormWindMin);
        var last = s.WindTarget;
        float heading = last.LengthSquared() > 1e-6f ? MathF.Atan2(last.Y, last.X) : 0.35f;
        heading += rng.NextSigned() * 0.6f;
        s.WindTarget = new Vector2(MathF.Cos(heading), MathF.Sin(heading)) * speed;
    }

    private void StrengthenWind(ref Pcg32 rng)
    {
        var s = State;
        if (s.WindTarget.Length() >= Config.StormWindMin) return;
        PickWind(ref rng);
    }

    private void Ease(int dt)
    {
        if (dt <= 0) return;
        var s = State;
        float target = s.Precipitation == Precipitation.None ? 0f : s.Intensity;
        float step = dt / (Math.Max(Config.FadeSeconds, 0.1f) * Common.Units.TicksPerSecond);
        s.Current = MoveToward(s.Current, target, step);
        float k = Math.Min(1f, dt / (Math.Max(Config.WindEaseSeconds, 0.1f) * Common.Units.TicksPerSecond));
        s.Wind += (s.WindTarget - s.Wind) * k;
    }

    /// <summary>Starts and stops the mirrored world events when the weather changes.</summary>
    private void Sync()
    {
        var s = State;
        var c = Config;
        Mirror(ref _rain, s.Precipitation != Precipitation.None, c.RainEvent);
        Mirror(ref _storm, s.Precipitation == Precipitation.Storm, c.StormEvent);
        Mirror(ref _windy, s.Windy, c.WindyEvent);
        Mirror(ref _sandstorm, s.Sandstorm, c.SandstormEvent);
    }

    private void Mirror(ref bool? last, bool want, string eventId)
    {
        if (last == want) return;
        last = want;
        var events = _world.Events;
        if (want == events.IsActive(eventId)) return;
        if (!want) events.Stop(eventId);
        else if (_world.Session.Content.Events.TryGet(eventId, out var def)) events.Start(def);
    }

    private static int Ticks(float hours) => Math.Max(1, (int)(hours * GameClock.TicksPerHour));

    private static float MoveToward(float from, float to, float step) =>
        from < to ? Math.Min(from + step, to) : Math.Max(from - step, to);
}
