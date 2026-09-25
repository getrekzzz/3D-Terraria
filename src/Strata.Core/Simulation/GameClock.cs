namespace Strata.Core.Simulation;

/// <summary>
/// World time. One in-game hour lasts 3600 ticks (one real minute), so a full day is 24 real minutes:
/// daytime 04:30–19:30 (15 min), night 19:30–04:30 (9 min), matching the reference game's rhythm.
/// </summary>
public sealed class GameClock
{
    public const int TicksPerHour = 3600;
    public const int TicksPerDay = TicksPerHour * 24;
    public const float DawnHour = 4.5f;
    public const float DuskHour = 19.5f;

    public GameClock(long tick = TicksPerHour * 8L) => Tick = tick;

    /// <summary>Absolute ticks since world creation (persisted).</summary>
    public long Tick { get; private set; }

    /// <summary>Multiplier for debug time-lapse (1 = normal).</summary>
    public int Speed { get; set; } = 1;

    public long Day => Tick / TicksPerDay;

    /// <summary>0 = full moon … 7.</summary>
    public int MoonPhase => (int)(Day % 8);

    public float Hour => (Tick % TicksPerDay) / (float)TicksPerHour;

    public bool IsDay => Hour >= DawnHour && Hour < DuskHour;

    /// <summary>0 at dawn → 1 at dusk during the day; 0 at dusk → 1 at dawn during the night.</summary>
    public float PhaseProgress
    {
        get
        {
            float h = Hour;
            if (IsDay) return (h - DawnHour) / (DuskHour - DawnHour);
            float sinceDusk = h >= DuskHour ? h - DuskHour : h + 24f - DuskHour;
            return sinceDusk / (24f - (DuskHour - DawnHour));
        }
    }

    public void Advance(int ticks = 1) => Tick += (long)ticks * Speed;

    public void SetHour(float hour)
    {
        long dayStart = Day * TicksPerDay;
        Tick = dayStart + (long)(((hour % 24f) + 24f) % 24f * TicksPerHour);
    }
}
