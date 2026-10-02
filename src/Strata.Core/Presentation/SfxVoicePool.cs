namespace Strata.Core.Presentation;

/// <summary>
/// Which player a new sound effect plays on: a fixed number of voices per category (2D or 3D) with voice stealing, so a
/// burst of sounds never drops the newest one silently and never plays more than the pool holds. Policy: a sound id may
/// hold at most <see cref="MaxPerId"/> voices; one more steals that id's oldest voice. Otherwise a free voice is used,
/// and when every voice is busy the oldest one is stolen (the quieter of equally old ones). Voices end by time (the
/// caller passes each sound's length), so the pool is deterministic and allocation-free after construction.
/// </summary>
public sealed class SfxVoicePool
{
    private readonly int[] _id;
    private readonly double _start;
    private readonly double[] _started;
    private readonly double[] _ends;
    private readonly float[] _gain;

    public SfxVoicePool(int voices, int maxPerId)
    {
        if (voices < 1) throw new ArgumentOutOfRangeException(nameof(voices));
        if (maxPerId < 1) throw new ArgumentOutOfRangeException(nameof(maxPerId));
        MaxPerId = maxPerId;
        _id = new int[voices];
        _started = new double[voices];
        _ends = new double[voices];
        _gain = new float[voices];
        _start = double.NegativeInfinity;
        Array.Fill(_ends, _start);
    }

    public int Count => _id.Length;
    public int MaxPerId { get; }

    /// <summary>Voices stolen so far (a busy voice cut short for a newer sound).</summary>
    public int Stolen { get; private set; }

    /// <summary>The most voices busy at once so far (never more than <see cref="Count"/>).</summary>
    public int PeakBusy { get; private set; }

    /// <summary>Voices still sounding at <paramref name="now"/> (seconds).</summary>
    public int Busy(double now)
    {
        int busy = 0;
        for (int i = 0; i < _ends.Length; i++)
            if (_ends[i] > now) busy++;
        return busy;
    }

    /// <summary>
    /// Picks the voice for sound <paramref name="id"/> (any stable key, such as a string hash) starting at
    /// <paramref name="now"/> for <paramref name="length"/> seconds at linear <paramref name="gain"/>, and marks it busy.
    /// <paramref name="stole"/> says whether a sounding voice was cut short.
    /// </summary>
    public int Acquire(int id, double now, double length, float gain, out bool stole)
    {
        int sameId = 0, oldestSame = -1, free = -1, oldest = -1;
        for (int i = 0; i < _id.Length; i++)
        {
            if (_ends[i] <= now)
            {
                if (free < 0) free = i;
                continue;
            }
            if (_id[i] == id)
            {
                sameId++;
                if (oldestSame < 0 || _started[i] < _started[oldestSame]) oldestSame = i;
            }
            if (oldest < 0 || _started[i] < _started[oldest] || _started[i] == _started[oldest] && _gain[i] < _gain[oldest]) oldest = i;
        }
        int voice = sameId >= MaxPerId ? oldestSame : free >= 0 ? free : oldest;
        stole = voice != free;
        if (stole) Stolen++;
        _id[voice] = id;
        _started[voice] = now;
        _ends[voice] = now + Math.Max(length, 0.01);
        _gain[voice] = gain;
        int busy = Busy(now);
        if (busy > PeakBusy) PeakBusy = busy;
        return voice;
    }

    /// <summary>Zeroes <see cref="Stolen"/> and <see cref="PeakBusy"/> (voices keep sounding).</summary>
    public void ResetCounters() => Stolen = PeakBusy = 0;

    /// <summary>Frees every voice (after the players were stopped).</summary>
    public void Clear() => Array.Fill(_ends, _start);
}
